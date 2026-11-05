namespace Lumina_Reserve_2.Services
{
    // Hora oficial de la barbería (Tuxtla Gutiérrez, Chiapas) sin importar la zona horaria
    // del servidor. En un hosting (Somee, Azure...) el servidor suele estar en UTC u otro país,
    // y DateTime.Now daría la hora equivocada para agendar, recordatorios y el resumen diario.
    // Equivale a dbo.fn_AhoraMexico() en la base de datos.
    public static class Reloj
    {
        private static readonly TimeZoneInfo Zona = BuscarZona();

        public static DateTime Ahora => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zona);
        public static DateTime Hoy => Ahora.Date;

        private static TimeZoneInfo BuscarZona()
        {
            // Windows usa "Central Standard Time (Mexico)"; Linux usa "America/Mexico_City".
            foreach (var id in new[] { "Central Standard Time (Mexico)", "America/Mexico_City" })
            {
                try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
                catch (TimeZoneNotFoundException) { }
                catch (InvalidTimeZoneException) { }
            }
            // Chiapas está en UTC-6 todo el año (México eliminó el horario de verano en 2022).
            return TimeZoneInfo.CreateCustomTimeZone("Mexico-Centro", TimeSpan.FromHours(-6), "Hora del Centro (México)", "Hora del Centro (México)");
        }
    }
}
