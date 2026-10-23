using StreetCorsa.Controls;

namespace StreetCorsa.Tests;

/// <summary>
/// The 3D's runtimes told to the player, not a black screen: what the check finds missing and the message it makes
/// </summary>
public class GraphicsTroubleTests
{
    [Fact]
    public void Every_runtime_file_that_does_not_load_is_missing()
    {
        var missing = GraphicsTrouble.MissingRuntimes(file => !file.StartsWith("d3d", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(["d3dx11_43.dll", "D3DCompiler_43.dll"], missing);
        Assert.Empty(GraphicsTrouble.MissingRuntimes(_ => true));
    }

    [Fact]
    public void The_message_names_each_runtime_missing_once_with_its_download()
    {
        Assert.Null(GraphicsTrouble.RuntimesMessage([]));

        var directx = GraphicsTrouble.RuntimesMessage(["d3dx11_43.dll", "D3DCompiler_43.dll"])!.Value;
        Assert.Contains("June 2010", directx.Text, StringComparison.Ordinal);
        Assert.Contains("id=8109", directx.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("vc_redist", directx.Text, StringComparison.Ordinal);
        Assert.Contains("d3dx11_43.dll, D3DCompiler_43.dll", directx.Text, StringComparison.Ordinal);

        var both = GraphicsTrouble.RuntimesMessage(["msvcp140.dll", "d3dx11_43.dll"])!.Value;
        Assert.Contains("vc_redist.x64.exe", both.Text, StringComparison.Ordinal);
        Assert.Contains("id=8109", both.Text, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(both.Title));
    }

    [Fact]
    public void This_machine_has_the_runtimes_the_3D_needs_or_CI_says_which_are_not()
    {
        // The test host loads SlimDX for the render tests: a missing runtime here is a machine set up short, told by name
        var missing = GraphicsTrouble.MissingRuntimes();
        Assert.DoesNotContain(missing, f => GraphicsTrouble.VcRuntime.Contains(f));
    }
}
