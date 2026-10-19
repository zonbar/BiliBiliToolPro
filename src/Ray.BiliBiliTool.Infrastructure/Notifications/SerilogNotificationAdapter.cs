using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ray.BiliBiliTool.Application.Contracts.Notifications;
using Serilog;

namespace Ray.BiliBiliTool.Infrastructure.Notifications;

/// <summary>
/// Default notification adapter that routes messages into the existing Serilog pipeline.
///
/// The optional <c>Notify:Title</c> setting prepends a custom title to every notification
/// text. Because all push channels consume this single composed message, a single setting
/// gives every channel a customizable message title without forking the sink packages.
/// </summary>
public class SerilogNotificationAdapter(
    ILogger<SerilogNotificationAdapter> logger,
    IConfiguration configuration
) : INotificationService
{
    private readonly ILogger<SerilogNotificationAdapter> _logger = logger;
    private readonly string? _customTitle = string.IsNullOrWhiteSpace(
        configuration["Notify:Title"]
    )
        ? null
        : configuration["Notify:Title"];

    public Task SendAsync(NotificationMessage message, string groupKey)
    {
        try
        {
            string text = $"{TitlePrefix}[NOTIFICATION] {message.Title}: {message.Body}";
            _logger.Log(MapLevel(message.Level), text);
        }
        catch (Exception ex)
        {
            LogFailure(ex);
        }

        return Task.CompletedTask;
    }

    public Task SendSummaryAsync(string title, SummaryLine[] lines, string groupKey)
    {
        try
        {
            _logger.LogInformation(FormatSummary(title, lines));
        }
        catch (Exception ex)
        {
            LogFailure(ex);
        }

        return Task.CompletedTask;
    }

    private string TitlePrefix => _customTitle is null ? string.Empty : $"[{_customTitle}] ";

    private string FormatSummary(string title, SummaryLine[] lines)
    {
        StringBuilder builder = new();
        builder.Append(TitlePrefix);
        builder.Append("[NOTIFICATION] ");
        builder.AppendLine(title);

        foreach (SummaryLine line in lines)
        {
            string icon = line.StatusIcon ?? "•";
            builder.AppendLine($"{icon} {line.Label}: {line.Value}");
        }

        return builder.ToString().TrimEnd();
    }

    private static LogLevel MapLevel(NotificationLevel level) =>
        level switch
        {
            NotificationLevel.Info => LogLevel.Information,
            NotificationLevel.Warning => LogLevel.Warning,
            NotificationLevel.Error => LogLevel.Error,
            _ => LogLevel.Information,
        };

    private static void LogFailure(Exception ex)
    {
        Log.ForContext("NotificationOrigin", "adapter")
            .Error(ex, "SerilogNotificationAdapter failed to dispatch notification");
    }
}
