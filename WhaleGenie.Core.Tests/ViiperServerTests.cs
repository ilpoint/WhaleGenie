using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Starting another project's server. Nothing here puts a program on the machine: the two things
/// that look outside — whether a server is answering and how one is launched — are handed in, so
/// the decisions can be read without a server being there.
/// </summary>
public class ViiperServerTests : IDisposable
{
    private readonly Func<bool> _answering = ViiperServer.Answering;
    private readonly Func<string, bool> _launch = ViiperServer.Launch;
    private readonly string? _executable = ViiperServer.Executable;

    /// <summary>Puts back what was there, so no other check runs against a made-up machine.</summary>
    public void Dispose()
    {
        ViiperServer.Answering = _answering;
        ViiperServer.Launch = _launch;
        ViiperServer.Executable = _executable;
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_server_that_is_already_there_is_left_alone()
    {
        // Something else — the user, or a run that finished — put a server there. Starting a second
        // one would give the machine two, and the one it is talking to is not ours to replace.
        var launched = 0;
        ViiperServer.Answering = () => true;
        ViiperServer.Launch = _ =>
        {
            launched++;
            return true;
        };
        ViiperServer.Executable = ProgramFile();

        Assert.True(ViiperServer.Ensure());
        Assert.Equal(0, launched);
    }

    [Fact]
    public void With_nothing_chosen_there_is_nothing_to_start()
    {
        var launched = 0;
        ViiperServer.Answering = () => false;
        ViiperServer.Launch = _ =>
        {
            launched++;
            return true;
        };
        ViiperServer.Executable = string.Empty;

        Assert.False(ViiperServer.Ensure());
        Assert.Equal(0, launched);
    }

    [Fact]
    public void A_chosen_file_that_is_not_there_is_not_started()
    {
        // The path is remembered and can go stale — the folder moved, or a drive is not plugged in.
        var launched = 0;
        ViiperServer.Answering = () => false;
        ViiperServer.Launch = _ =>
        {
            launched++;
            return true;
        };
        ViiperServer.Executable =
            Path.Combine(Path.GetTempPath(), $"no-viiper-{Guid.NewGuid():N}", "viiper.exe");

        Assert.False(ViiperServer.Ensure());
        Assert.Equal(0, launched);
    }

    [Fact]
    public void The_chosen_program_is_the_one_that_gets_started()
    {
        var path = ProgramFile();
        string? started = null;
        ViiperServer.Answering = () => false;
        ViiperServer.Launch = candidate =>
        {
            started = candidate;
            return true;
        };
        ViiperServer.Executable = path;

        Assert.True(ViiperServer.Ensure());
        Assert.Equal(path, started);
    }

    [Fact]
    public void A_server_that_does_not_come_up_is_reported_rather_than_promised()
    {
        ViiperServer.Answering = () => false;
        ViiperServer.Launch = _ => false;
        ViiperServer.Executable = ProgramFile();

        Assert.False(ViiperServer.Ensure());
    }

    [Fact]
    public void The_launch_carries_the_flag_that_attaches_the_devices()
    {
        // Without this flag the keyboard and mouse go on the bus and never reach the desktop, which
        // is the difference between a virtual keyboard that types and one that does nothing. The
        // flag itself is what the server documents; that the launch uses it is read here, because
        // running the real server is a thing for a person to do, not for a check.
        Assert.Equal("--api.auto-attach-local-client", ViiperServer.AutoAttach);
        Assert.Contains("Arguments = AutoAttach", Source(), StringComparison.Ordinal);
    }

    /// <summary>A file that is there, which is all the decision above asks of it.</summary>
    private static string ProgramFile() => typeof(ViiperServerTests).Assembly.Location;

    private static string Source()
        => File.ReadAllText(Path.Combine(Repository(), "WhaleGenie.Core", "Devices", "Platform",
            "ViiperServer.cs"));

    /// <summary>The repository root, found by walking up from the test binaries.</summary>
    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }
}
