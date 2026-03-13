namespace Botty.Cli.Infrastructure;

public class CliApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
