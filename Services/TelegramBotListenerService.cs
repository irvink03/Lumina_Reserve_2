using System.Security.Cryptography;
using Lumina_Reserve_2.Models;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Lumina_Reserve_2.Services
{
    public class TelegramBotListenerService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<TelegramBotListenerService> _log;

        public TelegramBotListenerService(IServiceScopeFactory scopeFactory, IConfiguration config,
                                          ILogger<TelegramBotListenerService> log)
        {
            _scopeFactory = scopeFactory;
            _config = config;
            _log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var token = _config["Telegram:BotToken"];
            if (!TelegramService.TokenUsable(token))
            {
                _log.LogWarning("Bot de Telegram desactivado: falta Telegram:BotToken.");
                return;
            }

            try
            {
                var bot = new TelegramBotClient(token!);
                var yo = await bot.GetMe(stoppingToken);
                _log.LogInformation("Bot de Telegram activo como @{Usuario}", yo.Username);

                bot.StartReceiving(
                    updateHandler: HandleUpdateAsync,
                    errorHandler: HandlePollingErrorAsync,
                    receiverOptions: new ReceiverOptions
                    {
                        AllowedUpdates = new[] { UpdateType.Message }
                    },
                    cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "No se pudo iniciar el bot de Telegram (revisa el token).");
            }
        }

        private async Task HandleUpdateAsync(ITelegramBotClient bot, Update update, CancellationToken ct)
        {
            if (update.Message is not { Text: { } texto } message) return;

            var chatId = message.Chat.Id;
            texto = texto.Trim();

            try
            {
                if (texto.Equals("/start", StringComparison.OrdinalIgnoreCase))
                {
                    await bot.SendMessage(chatId,
                        "👋 ¡Bienvenido al Bot de *Lumina Reserve*!\n\nEnvíame tu *correo electrónico registrado* en la plataforma para generar tu código de verificación.",
                        parseMode: ParseMode.Markdown, cancellationToken: ct);
                    return;
                }

                if (!EsCorreo(texto))
                {
                    await bot.SendMessage(chatId,
                        "Envíame únicamente tu correo registrado (ej. tucorreo@email.com) para darte tu código.",
                        cancellationToken: ct);
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var correo = texto.ToLowerInvariant();
                var usuario = await context.Usuarios
                    .FirstOrDefaultAsync(u => u.Correo.ToLower() == correo, ct);

                if (usuario == null || usuario.Estado == false)
                {
                    await bot.SendMessage(chatId,
                        "❌ Correo no encontrado. Verifica que esté bien escrito o regístrate primero en la web.",
                        cancellationToken: ct);
                    return;
                }

                if (usuario.TelegramChatId.HasValue && usuario.TelegramChatId.Value != chatId)
                {
                    await bot.SendMessage(chatId,
                        "⚠️ Esa cuenta ya está vinculada a otro Telegram. Si cambiaste de teléfono, contacta a la barbería.",
                        cancellationToken: ct);
                    return;
                }

                var ahora = Reloj.Ahora;

                var hayReciente = await context.CodigosVerificacionBots.AnyAsync(c =>
                    c.UsuarioId == usuario.UsuarioId &&
                    !c.Codigo.StartsWith("R") && // los códigos "R..." son de recuperación de contraseña
                    (c.Usado == false || c.Usado == null) &&
                    c.FechaExpiracion > ahora.AddMinutes(4), ct);
                if (hayReciente)
                {
                    await bot.SendMessage(chatId,
                        "⏳ Ya te envié un código hace un momento. Espera 1 minuto para pedir otro.",
                        cancellationToken: ct);
                    return;
                }

                var anteriores = await context.CodigosVerificacionBots
                    .Where(c => c.UsuarioId == usuario.UsuarioId && !c.Codigo.StartsWith("R") && (c.Usado == false || c.Usado == null))
                    .ToListAsync(ct);
                foreach (var a in anteriores) a.Usado = true;

                var codigo = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

                usuario.TelegramChatId = chatId;
                context.CodigosVerificacionBots.Add(new CodigosVerificacionBot
                {
                    UsuarioId = usuario.UsuarioId,
                    Codigo = codigo,
                    FechaExpiracion = ahora.AddMinutes(5),
                    Usado = false
                });
                await context.SaveChangesAsync(ct);

                await bot.SendMessage(chatId,
                    $"🔑 Tu código de verificación es: *{codigo}*\n\nVálido por 5 minutos. Regresa a la página e ingrésalo.\n\n🔔 Aquí recibirás los avisos de tus citas.",
                    parseMode: ParseMode.Markdown, cancellationToken: ct);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error procesando un mensaje del bot (chat {ChatId})", chatId);
            }
        }

        private Task HandlePollingErrorAsync(ITelegramBotClient bot, Exception ex, CancellationToken ct)
        {
            _log.LogWarning("Error de polling de Telegram: {Mensaje}", ex.Message);
            return Task.CompletedTask;
        }

        private static bool EsCorreo(string t) =>
            t.Length <= 254 && t.Contains('@') && t.Contains('.') && !t.Contains(' ');
    }
}