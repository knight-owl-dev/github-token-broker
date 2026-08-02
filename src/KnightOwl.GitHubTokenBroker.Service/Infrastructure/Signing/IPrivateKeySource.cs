namespace KnightOwl.GitHubTokenBroker.Service.Infrastructure.Signing;

/// <summary>
/// Supplies the App private key. This is the only seam through which key material
/// enters the process.
/// </summary>
public interface IPrivateKeySource
{
    /// <summary>Loads the current key.</summary>
    /// <returns>Key material the caller owns and must dispose.</returns>
    /// <remarks>
    /// Called once per mint rather than cached, so replacing the key file takes
    /// effect on the next mint with no restart and no window in which a disposed
    /// key is still in use.
    /// </remarks>
    PrivateKeyMaterial Load();
}
