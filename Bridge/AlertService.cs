using System;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Bridge
{
    /// <summary>
    /// Service for sending alerts via Slack, Telegram, or Email
    /// </summary>
    public class AlertService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<AlertService> _logger;
        private readonly HttpClient _httpClient;

        public AlertService(IConfiguration configuration, ILogger<AlertService> logger)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClient = new HttpClient();
        }

        public async Task SendAlertAsync(string title, string message, AlertLevel level = AlertLevel.Warning)
        {
            var enabled = _configuration.GetValue("Bridge:Alerts:Enabled", false);
            if (!enabled)
            {
                return;
            }

            try
            {
                var tasks = new System.Collections.Generic.List<Task>();

                // Send to Slack
                var slackWebhook = _configuration["Bridge:Alerts:SlackWebhookUrl"];
                if (!string.IsNullOrEmpty(slackWebhook))
                {
                    tasks.Add(SendSlackAlertAsync(slackWebhook, title, message, level));
                }

                // Send to Telegram
                var telegramToken = _configuration["Bridge:Alerts:TelegramBotToken"];
                var telegramChatId = _configuration["Bridge:Alerts:TelegramChatId"];
                if (!string.IsNullOrEmpty(telegramToken) && !string.IsNullOrEmpty(telegramChatId))
                {
                    tasks.Add(SendTelegramAlertAsync(telegramToken, telegramChatId, title, message, level));
                }

                // Send Email
                var emailHost = _configuration["Bridge:Alerts:EmailSmtpHost"];
                if (!string.IsNullOrEmpty(emailHost))
                {
                    tasks.Add(SendEmailAlertAsync(title, message, level));
                }

                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending alert");
            }
        }

        private async Task SendSlackAlertAsync(string webhookUrl, string title, string message, AlertLevel level)
        {
            try
            {
                var color = level switch
                {
                    AlertLevel.Info => "#36a64f",
                    AlertLevel.Warning => "#ff9900",
                    AlertLevel.Error => "#ff0000",
                    AlertLevel.Critical => "#8b0000",
                    _ => "#808080"
                };

                var payload = new
                {
                    attachments = new[]
                    {
                        new
                        {
                            color = color,
                            title = title,
                            text = message,
                            footer = "Bridge Alert",
                            ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                        }
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync(webhookUrl, content);
                response.EnsureSuccessStatusCode();
                
                _logger.LogInformation("Alert sent to Slack");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Slack alert");
            }
        }

        private async Task SendTelegramAlertAsync(string botToken, string chatId, string title, string message, AlertLevel level)
        {
            try
            {
                var emoji = level switch
                {
                    AlertLevel.Info => "ℹ️",
                    AlertLevel.Warning => "⚠️",
                    AlertLevel.Error => "❌",
                    AlertLevel.Critical => "🚨",
                    _ => "📢"
                };

                var text = $"{emoji} *{title}*\n\n{message}";
                var url = $"https://api.telegram.org/bot{botToken}/sendMessage";
                
                var payload = new
                {
                    chat_id = chatId,
                    text = text,
                    parse_mode = "Markdown"
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync(url, content);
                response.EnsureSuccessStatusCode();
                
                _logger.LogInformation("Alert sent to Telegram");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending Telegram alert");
            }
        }

        private async Task SendEmailAlertAsync(string title, string message, AlertLevel level)
        {
            try
            {
                var smtpHost = _configuration["Bridge:Alerts:EmailSmtpHost"];
                var smtpPort = _configuration.GetValue("Bridge:Alerts:EmailSmtpPort", 587);
                var username = _configuration["Bridge:Alerts:EmailUsername"];
                var password = _configuration["Bridge:Alerts:EmailPassword"];
                var toEmail = _configuration["Bridge:Alerts:EmailTo"];

                using var smtpClient = new SmtpClient(smtpHost, smtpPort)
                {
                    EnableSsl = true,
                    Credentials = new System.Net.NetworkCredential(username, password)
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(username),
                    Subject = $"[Bridge Alert - {level}] {title}",
                    Body = message,
                    IsBodyHtml = false
                };
                mailMessage.To.Add(toEmail);

                await smtpClient.SendMailAsync(mailMessage);
                _logger.LogInformation("Alert sent via email");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending email alert");
            }
        }
    }

    public enum AlertLevel
    {
        Info,
        Warning,
        Error,
        Critical
    }
}
