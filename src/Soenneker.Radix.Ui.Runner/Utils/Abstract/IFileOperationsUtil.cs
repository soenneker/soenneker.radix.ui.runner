using System.Threading;
using System.Threading.Tasks;

namespace Soenneker.Radix.Ui.Runner.Utils.Abstract;

/// <summary>
/// Refreshes the crawled Radix documentation repositories.
/// </summary>
public interface IFileOperationsUtil
{
    /// <summary>
    /// Crawls the Radix documentation and publishes the validated output.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the full processing workflow has finished.</returns>
    ValueTask Process(CancellationToken cancellationToken);
}
