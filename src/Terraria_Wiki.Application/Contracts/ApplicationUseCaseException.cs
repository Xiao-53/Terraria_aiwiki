namespace Terraria_Wiki.Application;

public sealed class ApplicationUseCaseException : Exception
{
    public ApplicationUseCaseException(string code, string message, Exception? innerException = null)
        : base(message, innerException) => Code = code;

    public string Code { get; }
}
