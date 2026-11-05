using Lumina_Reserve_2.Models;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types.Enums;

namespace Lumina_Reserve_2.Services
{
    public class TelegramService
    {
        private readonly TelegramBotClient? _botClient;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TelegramService> _log;

        public TelegramService(HttpClient httpClient, IConfiguration config,
                               IServiceScopeFactory scopeFactory, ILogger<TelegramService> log)
        {
            _scopeFactory = scopeFactory;
            _log = log;

            var token = config["Telegram:BotToken"];
            if (TokenUsable(token))
            {
                try { _botClient = new TelegramBotClient(token!, httpClient); }
                catch (Exception ex) { _log.LogError(ex, "El token de Telegram no tiene un formato válido."); }
            }
            else
            {
                _log.LogWarning("Telegram:BotToken no está configurado; no se enviarán mensajes.");
            }
        }

        // Un token real tiene la forma "<id numérico>:<secreto>"; así se ignoran los textos de ejemplo
        // como "TU TOKEN NUEVO AQUÍ" de appsettings.json.
        public static bool TokenUsable(string? token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            var partes = token.Split(':', 2);
            return partes.Length == 2 && partes[0].Length > 0 && partes[0].All(char.IsDigit) && partes[1].Length >= 20;
        }

        public async Task<bool> EnviarMensajeAsync(string chatId, string mensaje)
        {
            if (_botClient == null || string.IsNullOrWhiteSpace(chatId)) return false;

            try
            {
                await _botClient.SendMessage(chatId, mensaje, parseMode: ParseMode.Markdown);
                return true;
            }
            catch (ApiRequestException ex) when (ex.ErrorCode == 400)
            {
                try
                {
                    await _botClient.SendMessage(chatId, mensaje.Replace("*", "").Replace("_", " "));
                    return true;
                }
                catch (Exception ex2)
                {
                    _log.LogWarning(ex2, "Telegram rechazó el mensaje para el chat {ChatId}", chatId);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "No se pudo enviar el mensaje de Telegram al chat {ChatId}", chatId);
                return false;
            }
        }

        public async Task<bool> ValidarCodigoVerificacionAsync(int usuarioId, string codigoIngresado)
        {
            if (string.IsNullOrWhiteSpace(codigoIngresado)) return false;
            var codigo = codigoIngresado.Trim();

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ahora = Reloj.Ahora;

            var registro = await context.CodigosVerificacionBots
                .Where(c => c.UsuarioId == usuarioId
                         && c.Codigo == codigo
                         && !c.Codigo.StartsWith("R") // los códigos de recuperación no sirven para iniciar sesión
                         && (c.Usado == false || c.Usado == null)
                         && c.FechaExpiracion > ahora)
                .OrderByDescending(c => c.FechaExpiracion)
                .FirstOrDefaultAsync();

            if (registro == null) return false;

            registro.Usado = true;
            await context.SaveChangesAsync();
            return true;
        }
    }
}