-- =====================================================================
-- LUMINA RESERVE - HORA LOCAL DE MÉXICO
-- Azure SQL Database trabaja en UTC: ahí GETDATE() devuelve la hora UTC
-- (6 horas adelantada respecto a Tuxtla Gutiérrez). La aplicación guarda las
-- citas en hora local, así que los procedimientos usan esta función para
-- comparar contra "ahora" en la misma zona horaria.
-- En un SQL Server local configurado en México da lo mismo que GETDATE().
-- =====================================================================
CREATE OR ALTER FUNCTION dbo.fn_AhoraMexico()
RETURNS DATETIME
AS
BEGIN
    RETURN CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'Central Standard Time (Mexico)' AS DATETIME);
END;
GO
