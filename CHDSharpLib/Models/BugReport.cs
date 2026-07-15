namespace CHDSharp.Models;

internal sealed class BugReport
{
    public string Message { get; }
    public string ApplicationName { get; }
    public string Version { get; }
    public string UserInfo { get; }
    public string Environment { get; }
    public string StackTrace { get; }

    public BugReport(string message, string applicationName, string version,
        string userInfo, string environment, string stackTrace)
    {
        Message = message;
        ApplicationName = applicationName;
        Version = version;
        UserInfo = userInfo;
        Environment = environment;
        StackTrace = stackTrace;
    }
}
