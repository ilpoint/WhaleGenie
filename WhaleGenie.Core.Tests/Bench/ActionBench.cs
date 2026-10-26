using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Tests.Bench;

/// <summary>What one action came to.</summary>
internal enum BenchOutcome
{
    /// <summary>Built, ran, wrote what it had to, and the ledger agreed with the declaration.</summary>
    Pass,

    /// <summary>Built and ran, but something the catalogue says is not what happened.</summary>
    Fail,

    /// <summary>A parameter nobody could give a value to, so the step cannot run.</summary>
    NoFixture,

    /// <summary>Needs something this bench cannot give it; the reason is written down.</summary>
    Unreachable,
}

/// <summary>What a step says about the machine, which is what the ledger is held up against.</summary>
internal sealed record BenchDeclaration(bool SendsInput, bool LooksAtScreen);

/// <summary>One row of the matrix.</summary>
internal sealed record BenchCell(
    string Key,
    string Category,
    bool Hidden,
    BenchOutcome Outcome,
    string Detail,
    IReadOnlyList<string> Notes)
{
    public string State => Outcome switch
    {
        BenchOutcome.Pass => "通过",
        BenchOutcome.Fail => "不符",
        BenchOutcome.NoFixture => "缺夹具",
        _ => "不可达",
    };
}

/// <summary>
/// Runs one action against a stand-in desktop and says what happened. It does not change the
/// engine or the catalogue: it only reports, so a change can be measured before and after.
/// </summary>
internal static class ActionBench
{
    /// <summary>The verbs the device layer writes for a call that reads the screen or a picture.</summary>
    /// <remarks>
    /// Loading a picture from a file is not reading the screen, so it is not in here. What is in
    /// here is what a person would see the program do: look at the desktop.
    /// </remarks>
    private static readonly HashSet<string> Looking = new(StringComparer.Ordinal)
    {
        "pixel", "capture", "findAll", "detect", "ocr",
    };

    /// <summary>
    /// The verbs for a call that works a control: these are the actions that press something on the
    /// machine one way or another, which is what the input gate has to know about.
    /// </summary>
    private static readonly HashSet<string> Sending = new(StringComparer.Ordinal)
    {
        "keyPress", "keyDown", "keyUp", "hotkey", "type", "move", "along", "moveBy", "down", "up",
        "click", "scroll", "drag", "dragAlong", "gamepadButton", "gamepadConnect", "gamepadRelease",
        "clickElement",
    };

    /// <summary>Runs one action and says what it came to.</summary>
    /// <param name="declared">
    /// A declaration to use instead of the catalogue's own, so the bench itself can be checked:
    /// a ruler that cannot tell a wrong declaration from a right one measures nothing.
    /// </param>
    public static async Task<BenchCell> RunAsync(ActionSemantics action, BenchDeclaration? declared = null)
    {
        var says = declared ?? new BenchDeclaration(action.SendsInput, action.LooksAtScreen);
        var fixture = BenchFixtures.Build(action);
        if (fixture.Step is not { } step)
        {
            return new BenchCell(action.Key, action.Category.ToString(), action.Hidden,
                BenchOutcome.Unreachable, fixture.Unreachable ?? string.Empty, []);
        }

        if (fixture.Unfilled.Count > 0)
        {
            return new BenchCell(action.Key, action.Category.ToString(), action.Hidden,
                BenchOutcome.NoFixture, "没有值可给：" + string.Join(", ", fixture.Unfilled),
                ["缺少参数的夹具，所以这一步没有运行"]);
        }

        var devices = Desk();
        var store = Store();

        // A few waits are about something turning up after a look or two, not about ending. The
        // fixture makes it late, and the check below asks whether the step noticed.
        var later = Later(action.Key, devices);

        // The one action that waits for a key needs somebody to press it: the bench presses it a
        // moment after the run starts, which is what a person at the keyboard does.
        if (action.Key == "input.waitKey")
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(50);
                devices.HookEvents.Press("F5");
            });
        }
        var notes = new List<string>();
        var problems = new List<string>();

        var result = await new MacroRunner(store, new BenchHost(), devices, 0.02, BenchLibrary.Instance)
            .RunAsync([.. fixture.Prelude, Wrapped(step, fixture.Wrapper)]);

        if (result.Status != Status(fixture.Ending))
        {
            problems.Add($"结局是 {result.Status}（{result.Key}），应验的是 {fixture.Ending}");
        }

        Results(action, store, notes, problems);
        if (later is { } arrival)
        {
            notes.Add(arrival.Note);
            foreach (var key in arrival.Keys)
            {
                if (!store.Local.TryGet(key, out var value) || value.Kind == ValueKind.Null)
                {
                    problems.Add($"等在第三步才出现的 {key} 没有被交出来");
                }
            }
        }
        Ledger(says, devices, notes, problems);

        return new BenchCell(action.Key, action.Category.ToString(), action.Hidden,
            problems.Count == 0 ? BenchOutcome.Pass : BenchOutcome.Fail,
            problems.Count == 0 ? string.Empty : string.Join("；", problems), notes);
    }

    /// <summary>The ending the report expects for a step that did what it says.</summary>
    private static RunStatus Status(BenchEnding ending) => ending switch
    {
        BenchEnding.Stopped => RunStatus.Stopped,
        BenchEnding.Failed => RunStatus.Failed,
        _ => RunStatus.Completed,
    };

    /// <summary>The step, inside whatever it needs to mean anything.</summary>
    private static ExecutableStep Wrapped(ExecutableStep step, BenchWrapper wrapper) => wrapper switch
    {
        BenchWrapper.Repeat => new ExecutableStep
        {
            Type = "control.repeat",
            Parameters =
            [
                new ExecutableParameter { Name = "times", Text = "1" },
                new ExecutableParameter { Name = "intervalMs", Text = "0" },
                new ExecutableParameter { Name = "body", Steps = [step] },
            ],
        },
        BenchWrapper.Switch => new ExecutableStep
        {
            Type = "control.switch",
            Parameters =
            [
                new ExecutableParameter { Name = "switchValue", Text = "bench" },
                new ExecutableParameter { Name = "switchMode", Text = "equals" },
                new ExecutableParameter { Name = "cases", Steps = [WithValue(step, "bench")] },
            ],
        },
        BenchWrapper.Catch => new ExecutableStep
        {
            Type = "control.try",
            Parameters =
            [
                new ExecutableParameter
                {
                    Name = "body",
                    Steps =
                    [
                        new ExecutableStep
                        {
                            Type = "control.fail",
                            Parameters = [new ExecutableParameter { Name = "reason", Text = "bench" }],
                        },
                    ],
                },
                new ExecutableParameter { Name = "catchWhen", Text = "true" },
                new ExecutableParameter { Name = "catch", Steps = [step] },
            ],
        },
        _ => step,
    };

    /// <summary>The same step with its <c>values</c> filled in, for the one action that needs them.</summary>
    private static ExecutableStep WithValue(ExecutableStep step, string value) => new()
    {
        Type = step.Type,
        Id = step.Id,
        Meta = step.Meta,
        Parameters =
        [
            .. step.Parameters.Select(parameter => parameter.Name == "values"
                ? new ExecutableParameter { Name = parameter.Name, Text = value }
                : parameter),
        ],
    };

    /// <summary>
    /// What the step handed over. The result it is usually asked for has to be there — a step whose
    /// main answer is missing has done nothing a macro can use — and whatever is there has to be the
    /// kind the catalogue says. A result that only some settings produce is noted instead of
    /// failing: the same action cannot hand over a window place when it never had a window.
    /// </summary>
    private static void Results(ActionSemantics action, VariableStore store, List<string> notes,
        List<string> problems)
    {
        if (action.Returns.Count == 0)
        {
            return;
        }

        var absent = new List<string>();
        var kept = 0;

        foreach (var declared in action.Returns)
        {
            var name = BenchFixtures.Name(declared.Key);
            if (!store.Local.TryGet(name, out var value))
            {
                absent.Add(declared.Key);
                continue;
            }

            // An empty answer is how a record or a list comes back when the attempt found
            // nothing: the engine's own rule, so the bench reads it the same way.
            if (value.IsNull || (value.Kind == ValueKind.Text && value.Text.Length == 0))
            {
                absent.Add(declared.Key);
                continue;
            }

            kept++;
            if (!Fits(declared.Type, value))
            {
                problems.Add($"结果 {declared.Key} 应为 {declared.Type}，写出来的是 {value.Kind}");
            }
        }

        if (kept == 0)
        {
            problems.Add("声明了结果，却一个都没交出来");
        }

        notes.Add($"结果 {kept}/{action.Returns.Count}");
        if (absent.Count > 0)
        {
            notes.Add("这次设置没写出：" + string.Join(",", absent));
        }
    }

    /// <summary>What a wait was told to wait for, and which results have to be there when it ends.</summary>
    private sealed record Arrival(string Note, string[] Keys);

    /// <summary>
    /// Makes the thing a wait waits for show up on the third look rather than the first, and says
    /// which results then have to be filled in. A wait that ends on its first look proves only that
    /// the condition was already true; these are the steps whose whole job is to wait.
    /// </summary>
    private static Arrival? Later(string key, FakeDeviceLayer devices) => key switch
    {
        "vision.waitImage" => Third(devices, () => devices.MatchAfter = 3, "match",
            "图在第三次查找才出现"),
        "vision.waitDetect" => Third(devices, () => devices.DetectAfter = 3, "match",
            "检测在第三次才找到"),
        "window.waitFor" => Third(devices, () =>
        {
            devices.WindowAppearsLater = "Bench";
            devices.WindowAppearsAfter = 3;
        }, "window", "窗口在第三次列窗口才出现"),
        _ => null,
    };

    /// <summary>One arrival: the switch to set, and the results that have to come back filled in.</summary>
    private static Arrival Third(FakeDeviceLayer devices, Action later, string result, string note)
    {
        later();
        _ = devices;
        return new Arrival(note, [BenchFixtures.Name(result)]);
    }

    /// <summary>Whether a value is the kind a declared result says it is.</summary>
    private static bool Fits(ActionReturnType type, Value value) => type switch
    {
        ActionReturnType.Number => value.Kind == ValueKind.Number,
        ActionReturnType.Flag => value.Kind == ValueKind.Bool,
        ActionReturnType.List => value.Kind == ValueKind.List,
        ActionReturnType.Record => value.Kind == ValueKind.Record,
        _ => value.Kind is ValueKind.Text or ValueKind.Number or ValueKind.Bool,
    };

    /// <summary>
    /// What the declaration says about the machine against what the machine was actually asked for.
    /// Both directions matter: a step that says it sends input and does not is a lie in the
    /// catalogue, and a step that sends it without saying so is what the input gate gets wrong.
    /// </summary>
    private static void Ledger(BenchDeclaration action, FakeDeviceLayer devices, List<string> notes,
        List<string> problems)
    {
        var verbs = devices.Calls.Select(Verb).ToHashSet(StringComparer.Ordinal);
        var sent = verbs.Overlaps(Sending) || devices.Routes.Count > 0;
        var looked = verbs.Overlaps(Looking);

        if (action.SendsInput && !sent)
        {
            problems.Add("声明会发输入，账本里没有任何一次输入");
        }

        if (!action.SendsInput && sent)
        {
            problems.Add("没有声明会发输入，账本里却有输入：" + string.Join(",", verbs.Intersect(Sending)));
        }

        if (action.LooksAtScreen && !looked)
        {
            problems.Add("声明会看屏幕，账本里没有读屏幕的调用");
        }

        if (!action.LooksAtScreen && looked)
        {
            problems.Add("没有声明会看屏幕，账本里却有读屏幕的调用：" + string.Join(",", verbs.Intersect(Looking)));
        }

        notes.Add($"设备调用 {devices.Calls.Count} 次");
    }

    /// <summary>The first word of a call the device layer noted.</summary>
    private static string Verb(string call)
    {
        var space = call.IndexOf(' ');
        return space < 0 ? call : call[..space];
    }

    /// <summary>
    /// The values a run starts with: one list to take apart, one list of rows, and one global. Every
    /// action that works on an existing value needs it to exist, which is something the macro author
    /// would have arranged before the step ran.
    /// </summary>
    private static VariableStore Store()
    {
        var store = new VariableStore();
        store.Set(BenchFixtures.ListName,
            Value.FromList([Value.FromText("bench"), Value.FromText("second")]));
        store.Set(BenchFixtures.RowsName,
            Value.FromList([Value.FromRecord([("a", Value.FromText("1")), ("b", Value.FromText("2"))])]));
        store.Set(BenchFixtures.GlobalName, Value.FromNumber(0), VariableTarget.Global);
        return store;
    }

    /// <summary>
    /// A desk with one window, one element, one file of each kind, one colour and one picture: what
    /// each action needs to have something to act on, all of it the same everywhere so a row can be
    /// compared with the row above it.
    /// </summary>
    private static FakeDeviceLayer Desk()
    {
        var devices = new FakeDeviceLayer
        {
            Cursor = new ScreenPoint(10, 10),
            Pixel = new PixelColor(255, 255, 255),
            ElementText = "bench",
            ClipboardText = "bench",
            ClipboardMovesAfter = 1,
            ClipboardCopy = new ImageFrame(8, 8, new byte[8 * 8 * 4]),
            Script = new ScriptResult(0, "bench", string.Empty, Value.FromText("bench")),
            CSharp = new ScriptResult(0, "bench", string.Empty, Value.FromText("bench")),
            AutoHotkey = new ScriptResult(0, "bench", string.Empty, Value.FromText("bench")),
            Command = new CommandResult(0, "bench", string.Empty),
        };

        devices.Desktop = new ScreenLayout(new ScreenArea(0, 0, 1920, 1080),
            [new ScreenDisplay(new ScreenArea(0, 0, 1920, 1080), true)]);

        devices.Files[BenchFixtures.TextFile] = "bench";
        devices.Files[BenchFixtures.OtherTextFile] = "bench";
        devices.Files[BenchFixtures.JsonFile] = """{"a":{"b":1}}""";
        devices.Files[BenchFixtures.CsvFile] = "a,b\r\n1,2";
        devices.Files[BenchFixtures.VarsFile] = """{"benchGlobal":"saved"}""";
        devices.Files[devices.Resolve(BenchFixtures.TextFile)] = "bench";
        devices.Files[devices.Resolve(BenchFixtures.OtherTextFile)] = "bench";
        devices.Files[devices.Resolve(BenchFixtures.JsonFile)] = """{"a":{"b":1}}""";
        devices.Files[devices.Resolve(BenchFixtures.CsvFile)] = "a,b\r\n1,2";
        devices.Files[devices.Resolve(BenchFixtures.VarsFile)] = """{"benchGlobal":"saved"}""";
        devices.Folders.Add(BenchFixtures.Folder);
        devices.Folders.Add(devices.Resolve(BenchFixtures.Folder));

        devices.Windows.Add(new WindowInfo(4242, "Bench", new ScreenPoint(100, 100),
            new ScreenSize(640, 480), false, false));
        devices.WindowFacts[4242] = (BenchFixtures.ProgramName, "Bench");
        devices.ClientSizes[4242] = new ScreenSize(640, 480);
        devices.Elements.Add(new UiElementInfo("Bench", "bench", "Button", "Bench",
            new ScreenPoint(120, 120), new ScreenSize(80, 24), "Bench"));
        devices.Match = new ImageMatch(0.99, new ScreenPoint(200, 200), new ScreenSize(64, 64));
        devices.Matches.Add(new ImageMatch(0.95, new ScreenPoint(300, 300), new ScreenSize(64, 64)));
        devices.Spans.Add(new TextSpan("Bench", new ScreenPoint(200, 200), new ScreenSize(60, 20), 0.9));
        devices.Detections.Add(new DetectionHit(0.9, new ScreenPoint(200, 200), new ScreenSize(40, 40),
            0, "bench"));
        devices.Running[BenchFixtures.ProgramName] = [BenchFixtures.ProcessId];
        devices.ExitCodes[BenchFixtures.ProcessId] = 0;
        devices.Exited.Add(BenchFixtures.ProcessId);
        devices.SystemValues["PATH"] = "C:\\bench";
        devices.SystemValues["userName"] = "bench";
        devices.EnvironmentValues["PATH"] = "C:\\bench";
        return devices;
    }
}
