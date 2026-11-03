using Lumina_Reserve_2.Models;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

namespace Lumina_Reserve_2.Services
{
    public class TelegramService
    {
        private readonly HttpClient _httpClient;
        private readonly TelegramBotClient? _botClient;
        private readonly IServiceScopeFactory _scopeFactory;

        // CORRECCIÓN: El constructor ahora recibe HttpClient como primer parámetro para satisfacer AddHttpClient<TelegramService>()
        public TelegramService(HttpClient httpClient, IConfiguration config, IServiceScopeFactory scopeFactory)
        {
            _httpClient = httpClient;
            _scopeFactory = scopeFactory;

            var token = config["Telegram:BotToken"];
            
            if (!string.IsNullOrWhiteSpace(token) && token != "TU_TOKEN_AQUI")
            {
                try
                {
                    _botClient = new TelegramBotClient(token);
                }
                catch
                {
                    _botClient = null;
                }
            }
            else
            {
                _botClient = null;
            }
        }

        public async Task EnviarMensajeAsync(string chatId, string mensaje)
        {
            if (_botClient != null && !string.IsNullOrEmpty(chatId))
            {
                try
                {
                    await _botClient.SendMessage(
                        chatId: chatId, 
                        text: mensaje, 
                        parseMode: ParseMode.Markdown);
                }
                catch (Exception)
                {
                    // Manejo silencioso de errores
                }
            }
        }

        public async Task<bool> ValidarCodigoVerificacionAsync(int usuarioId, string codigoIngresado)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            
            var registroCodigo = await context.CodigosVerificacionBots
                .Where(c => c.UsuarioId == usuarioId && c.Codigo == codigoIngresado &&
                            (c.Usado == false || c.Usado == null))
                .OrderByDescending(c => c.FechaExpiracion)
                .FirstOrDefaultAsync();

            if (registroCodigo == null) return false;

            registroCodigo.Usado = true;
            await context.SaveChangesAsync();
            return true;
        }
    }
}