using Microsoft.EntityFrameworkCore;
using Lumina_Reserve_2.Models;

namespace Lumina_Reserve_2.Services
{
    public class RecordatorioCitasService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public RecordatorioCitasService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("--> Servicio de recordatorios de citas iniciado correctamente.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await EnviarRecordatoriosAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error en ciclo de recordatorios: {ex.Message}");
                }

                // Espera 15 minutos antes de la siguiente revisión
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
        }

        private async Task EnviarRecordatoriosAsync()
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var telegramService = scope.ServiceProvider.GetRequiredService<TelegramService>();

                var ahora = DateTime.Now;
                var limite = ahora.AddHours(2);

                var citasProximas = await context.Citas
                    .Include(c => c.Usuario)
                    .Where(c => c.Estado == "Pendiente" 
                             && !c.RecordatorioEnviado 
                             && c.FechaHora > ahora 
                             && c.FechaHora <= limite)
                    .ToListAsync();

                foreach (var cita in citasProximas)
                {
                    if (cita.Usuario != null && cita.Usuario.TelegramChatId.HasValue)
                    {
                        string mensaje = $"⏳ *Lumina Reserve - Recordatorio*\n\n¡Hola {cita.Usuario.Nombre}! Te recordamos que tienes una cita programada para hoy a las {cita.FechaHora:HH:mm}. ¡Te esperamos!";
                        await telegramService.EnviarMensajeAsync(cita.Usuario.TelegramChatId.Value.ToString(), mensaje);
                    }
                    cita.RecordatorioEnviado = true;
                }

                if (citasProximas.Any())
                {
                    await context.SaveChangesAsync();
                }
            }
        }
    }
}