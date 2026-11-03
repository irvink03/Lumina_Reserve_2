using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Polling;
using Telegram.Bot.Types.Enums;
using Lumina_Reserve_2.Models;

namespace Lumina_Reserve_2.Services
{
    public class TelegramBotListenerService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _config;
        private TelegramBotClient? _botClient;

        public TelegramBotListenerService(IServiceScopeFactory scopeFactory, IConfiguration config)
        {
            _scopeFactory = scopeFactory;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var token = _config["Telegram:BotToken"];
            if (string.IsNullOrWhiteSpace(token) || token == "TU_TOKEN_AQUI")
            {
                return;
            }

            try
            {
                _botClient = new TelegramBotClient(token);

                var receiverOptions = new ReceiverOptions
                {
                    AllowedUpdates = Array.Empty<UpdateType>()
                };

                // Iniciamos la escucha de mensajes en segundo plano (Long Polling)
                _botClient.StartReceiving(
                    updateHandler: HandleUpdateAsync,
                    errorHandler: HandlePollingErrorAsync,
                    receiverOptions: receiverOptions,
                    cancellationToken: stoppingToken
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al iniciar el bot de Telegram: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Message is not { Text: { } messageText } message)
                return;

            var chatId = message.Chat.Id;
            messageText = messageText.Trim();

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // Si el usuario escribe /start
            if (messageText.Equals("/start", StringComparison.OrdinalIgnoreCase))
            {
                await botClient.SendMessage(
                    chatId: chatId,
                    text: "👋 ¡Bienvenido al Bot de *Lumina Reserve*!\n\nPor favor, envíame tu **correo electrónico registrado** en la plataforma para generarte tu código de verificación de acceso.",
                    parseMode: ParseMode.Markdown,
                    cancellationToken: cancellationToken
                );
                return;
            }

            // Si el texto contiene un correo electrónico
            if (messageText.Contains("@") && messageText.Contains("."))
            {
                var usuario = await context.Usuarios
                    .FirstOrDefaultAsync(u => u.Correo != null && u.Correo.ToLower() == messageText.ToLower(), cancellationToken);

                if (usuario != null)
                {
                    // Guardamos o actualizamos el ChatId del usuario
                    usuario.TelegramChatId = chatId;

                    // Generamos código aleatorio de 6 dígitos
                    string codigoAleatorio = new Random().Next(100000, 999999).ToString();

                    // Guardamos el código con vigencia de 5 minutos
                    var codigoVerificacion = new CodigosVerificacionBot
                    {
                        UsuarioId = usuario.UsuarioId,
                        Codigo = codigoAleatorio,
                        FechaExpiracion = DateTime.Now.AddMinutes(5),
                        Usado = false
                    };

                    context.CodigosVerificacionBots.Add(codigoVerificacion);
                    await context.SaveChangesAsync(cancellationToken);

                    // Respondemos al usuario en Telegram
                    await botClient.SendMessage(
                        chatId: chatId,
                        text: $"🔑 Tu código de verificación es: *{codigoAleatorio}*\n\nVálido por 5 minutos.\nRegresa a la página e ingrésalo para continuar.\n\n🔔 Desde ahora recibirás aquí los avisos de tus citas.",
                        parseMode: ParseMode.Markdown,
                        cancellationToken: cancellationToken
                    );
                }
                else
                {
                    await botClient.SendMessage(
                        chatId: chatId,
                        text: "❌ Correo no encontrado en nuestro sistema. Por favor, verifica que esté bien escrito o regístrate primero en la web.",
                        cancellationToken: cancellationToken
                    );
                }
            }
        }

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}