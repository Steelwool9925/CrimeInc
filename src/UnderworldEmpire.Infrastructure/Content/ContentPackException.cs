namespace UnderworldEmpire.Infrastructure.Content;

/// <summary>
/// Thrown when a content pack fails to load or fails validation.
/// The <see cref="Exception.Message"/> lists what is missing or invalid.
/// </summary>
public sealed class ContentPackException : Exception
{
    public ContentPackException(string message) : base(message) { }
    public ContentPackException(string message, Exception inner) : base(message, inner) { }
}
