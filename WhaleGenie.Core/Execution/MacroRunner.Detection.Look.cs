using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Execution;

// One look for objects: the query, what a sighting says, and the hits behind it.
public sealed partial class MacroRunner
{
    /// <summary>The file whose lines name the model's classes, or empty when the step named none.</summary>
    private string LabelFileOf(ExecutableStep step)
    {
        var named = PathOf(step, "labelFile").Trim();
        return named.Length == 0 ? string.Empty : _devices.Files.Resolve(named);
    }

    /// <summary>What a step asked the model to look for, as the device reads it.</summary>
    private DetectionQuery Detection(ExecutableStep step)
    {
        var confidence = Args(step).Number("confidence");
        var overlap = Args(step).Number("iou");
        var input = Args(step).Number("inputSize");

        // Every box the model found is collected, whatever the step will take out of them. Looking
        // for more costs nothing here — one pass of the model answers with all of them — while asking
        // for as few as the step needs would leave the device to cut the list down by score before
        // the step's own 「命中排序」 and 「第几个命中」 had their say, so a step counting in reading
        // order would have been handed whichever box scored highest. The whole list is also what the
        // look hands over, which is the point of the window.
        return new DetectionQuery(
            confidence > 0 ? confidence : 25,
            input >= 32 ? input : 640,
            overlap > 0 ? overlap : 45,
            Rows(Args(step).Text("labels")),
            MatchLimit,
            LabelFileOf(step));
    }

    /// <summary>
    /// What a step was after, as the user wrote it: which model, and which classes of thing. It is
    /// what a step that found nothing says it did not find, and what the look hands over as what was
    /// being looked for.
    /// </summary>
    private string Looking(ExecutableStep step)
    {
        var model = System.IO.Path.GetFileName(ModelOf(step));
        var labels = Rows(Args(step).Text("labels"));
        return labels.Count == 0 ? model : $"{model} {string.Join(',', labels)}";
    }

    /// <summary>One thing that was found, in the words a log line reads it in: person 640,360.</summary>
    private static string Saying(Sighting sighting)
        => sighting.Label.Length > 0
            ? $"{sighting.Label} {sighting.Match.Center.X},{sighting.Match.Center.Y}"
            : $"{sighting.Match.Center.X},{sighting.Match.Center.Y}";

    /// <summary>The rows of a list a step wrote: one per line, or separated by semicolons.</summary>
    private IReadOnlyList<string> Rows(string text)
        =>
        [
            .. text.Split([';', '\n', '\r'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        ];

    /// <summary>
    /// Every place the reference picture appears in the search areas, in screen coordinates and in
    /// reading order: down the screen first, then across. That is the order a person counts them in
    /// when looking at a screenshot, which is what "the third one" has to mean to be useful.
    /// Which of the pictures the step listed it goes with is the step's own choice: the first one
    /// that turns up, whichever scored closest, or all of them at once. The hits that come back are
    /// the hits of the one picture the step went with, and "the third one" counts inside it.
    /// </summary>
    private List<ImageMatch> Hits(ExecutableStep step)
    {
        var query = SearchQuery(step);
        var areas = SearchAreas(step);
        var wanted = References(step);

        // One picture, looked for in every area the step named: the places it turned up, in the
        // order this step counts them in.
        List<ImageMatch> Look(int number)
        {
            var hits = new List<ImageMatch>();
            foreach (var (area, origin) in areas)
            {
                // 匹配是整个宏里最贵的一次同步调用（整屏找图中位数二百毫秒），它中途读不到取消
                // 令牌；至少在每块区域前后问一次，让「停止」在两块之间就能生效。
                _run.RunToken.ThrowIfCancellationRequested();
                foreach (var found in _devices.Vision.FindAll(area, wanted[number].Frame, query))
                {
                    hits.Add(found with
                    {
                        Location = new ScreenPoint(
                            found.Location.X + origin.X, found.Location.Y + origin.Y),
                    });
                }
            }

            Sorted(hits, step);
            return hits;
        }

        // What the window is shown: the picture the step went with, or the first one it listed when
        // it went with none of them. Handing over nothing at all would leave out what was being
        // looked for and where.
        void SawPicture(int number, IReadOnlyList<ImageMatch> hits, bool found)
            => SawSearch(step, LookKind.Template, areas,
                [.. hits.Select(hit => (hit, (string?)null))],
                found ? WentWith(hits, step) : 0, wanted[number].Name, wanted[number].Frame,
                query.ConfidencePercent / 100, number + 1, wanted.Count);

        switch (WhichOfThePictures(step))
        {
            case "every":
                {
                    // The whole set has to be there. The step goes with the picture it listed
                    // first, which is the one whose hits are handed over.
                    var looked = new List<ImageMatch>[wanted.Count];
                    for (var number = 0; number < wanted.Count; number++)
                    {
                        looked[number] = Look(number);
                        if (looked[number].Count == 0)
                        {
                            SawPicture(0, [], found: false);
                            return [];
                        }
                    }

                    SawPicture(0, looked[0], found: true);
                    return looked[0];
                }

            case "best":
                {
                    // Every picture is looked at, and the one whose closest match scored highest
                    // is the one the step goes with.
                    var looked = new List<ImageMatch>[wanted.Count];
                    var winner = -1;
                    var surest = double.NegativeInfinity;
                    for (var number = 0; number < wanted.Count; number++)
                    {
                        looked[number] = Look(number);
                        var score = looked[number].Count == 0
                            ? double.NegativeInfinity
                            : looked[number].Max(hit => hit.Score);
                        if (score > surest)
                        {
                            surest = score;
                            winner = number;
                        }
                    }

                    if (winner < 0)
                    {
                        SawPicture(0, [], found: false);
                        return [];
                    }

                    SawPicture(winner, looked[winner], found: true);
                    return looked[winner];
                }

            default:
                {
                    // The first picture that turns up is the one the step goes with, and the rest
                    // of the list is not looked at at all.
                    for (var number = 0; number < wanted.Count; number++)
                    {
                        var hits = Look(number);
                        if (hits.Count == 0)
                        {
                            continue;
                        }

                        SawPicture(number, hits, found: true);
                        return hits;
                    }

                    SawPicture(0, [], found: false);
                    return [];
                }
        }
    }
}
