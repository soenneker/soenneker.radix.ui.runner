using Soenneker.Tests.HostedUnit;

namespace Soenneker.Radix.Ui.Runner.Tests;

[ClassDataSource<Host>(Shared = SharedType.PerTestSession)]
public sealed class RadixUiRunnerTests : HostedUnitTest
{
    public RadixUiRunnerTests(Host host) : base(host)
    {
    }

    [Test]
    public void Default()
    {

    }
}
