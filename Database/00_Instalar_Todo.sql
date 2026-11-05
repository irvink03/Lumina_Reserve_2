-- =====================================================================
-- LUMINA RESERVE - SCRIPT ÚNICO DE PROCEDIMIENTOS (local o Azure SQL)
-- Generado a partir de los archivos de la carpeta Database, en este orden:
--   1. fn_AhoraMexico.sql        hora local de México (Azure SQL trabaja en UTC)
--   2. sp_GestionarCorte.sql     catálogo de servicios
--   3. sp_PanelAdmin.sql         dashboard, agenda, gastos, estados
--   4. sp_PersonalYResumen.sql   alta/edición de barberos y resumen diario
--   5. sp_RecuperarPassword.sql  recuperación de contraseña por Telegram
--
-- Requisitos: la base ya debe tener las tablas del proyecto (Usuarios, Citas,
-- Cortes, Empleados, Pagos, Gastos, Notificaciones, CodigosVerificacionBot...).
-- No crea ni modifica tablas ni datos; solo funciones y procedimientos.
-- Todo usa CREATE OR ALTER, así que se puede ejecutar más de una vez.
--
-- Ejecutar conectado a la base de destino (en Azure no se usa USE <base>).
-- =====================================================================

-- #####################################################################
-- fn_AhoraMexico.sql
-- #####################################################################
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

-- #####################################################################
-- sp_GestionarCorte.sql
-- #####################################################################
-- ==========================================================
-- PROCEDIMIENTO ALMACENADO PARA GESTIONAR CORTES / SERVICIOS
-- @Opcion 1: Insertar o actualizar
--   - Con @CorteId > 0 actualiza ese registro por ID (permite renombrar).
--   - Sin @CorteId mantiene el comportamiento original: upsert por @Nombre.
-- @Opcion 2: Listar servicios activos
-- ==========================================================
CREATE OR ALTER PROCEDURE dbo.sp_GestionarCorte
    @Opcion INT,
    @Nombre VARCHAR(100) = NULL,
    @Descripcion VARCHAR(255) = NULL,
    @Precio DECIMAL(18,2) = 0.00,
    @DuracionMinutos INT = 30,
    @Estado BIT = 1,
    @CorteId INT = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Opcion = 1
    BEGIN
        IF @CorteId IS NOT NULL AND @CorteId > 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM dbo.Cortes WHERE CorteID = @CorteId)
            BEGIN
                RAISERROR('El servicio que intentas editar no existe.', 16, 1);
                RETURN;
            END

            IF EXISTS (SELECT 1 FROM dbo.Cortes WHERE Nombre = @Nombre AND CorteID <> @CorteId)
            BEGIN
                RAISERROR('Ya existe otro servicio con ese nombre.', 16, 1);
                RETURN;
            END

            UPDATE dbo.Cortes
            SET Nombre = @Nombre, Descripcion = @Descripcion, Precio = @Precio,
                DuracionMinutos = @DuracionMinutos, Estado = @Estado
            WHERE CorteID = @CorteId;
        END
        -- Comportamiento original: si ya existe un corte con el mismo nombre, se actualiza; si no, se inserta
        ELSE IF EXISTS (SELECT 1 FROM dbo.Cortes WHERE Nombre = @Nombre)
        BEGIN
            UPDATE dbo.Cortes
            SET Descripcion = @Descripcion, Precio = @Precio, DuracionMinutos = @DuracionMinutos, Estado = @Estado
            WHERE Nombre = @Nombre;
        END
        ELSE
        BEGIN
            INSERT INTO dbo.Cortes (Nombre, Descripcion, Precio, DuracionMinutos, Estado)
            VALUES (@Nombre, @Descripcion, @Precio, @DuracionMinutos, @Estado);
        END
    END
    ELSE IF @Opcion = 2
    BEGIN
        SELECT * FROM dbo.Cortes WHERE Estado = 1;
    END
END;
GO

-- #####################################################################
-- sp_PanelAdmin.sql
-- #####################################################################
-- =====================================================================
-- LUMINA RESERVE - PROCEDIMIENTOS DEL PANEL DE ADMINISTRACIÓN
-- Ejecutar sobre LuminaReserveDB. Todos usan CREATE OR ALTER, así que
-- el script se puede correr las veces que sea necesario.
--
-- Convención de fechas: @Desde y @Hasta son DATE e INCLUSIVOS
-- (se filtra FechaHora >= @Desde y FechaHora < @Hasta + 1 día).
-- Ingresos = pagos con EstadoPago = 'Completado'.
-- =====================================================================

-- ---------------------------------------------------------------------
-- 1. Resumen (KPIs) del dashboard por sucursal y periodo
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_DashboardResumen
    @SucursalID INT,
    @Desde DATE,
    @Hasta DATE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Ini DATETIME = CAST(@Desde AS DATETIME);
    DECLARE @Fin DATETIME = DATEADD(DAY, 1, CAST(@Hasta AS DATETIME));

    ;WITH C AS (
        SELECT CitaID, UsuarioID, Estado, NombreClienteExterno, TelefonoExterno
        FROM dbo.Citas
        WHERE SucursalID = @SucursalID AND FechaHora >= @Ini AND FechaHora < @Fin
    )
    SELECT x.*,
           x.Ingresos - x.Gastos AS Utilidad,
           CAST(CASE WHEN x.Completadas > 0 THEN x.Ingresos / x.Completadas ELSE 0 END AS DECIMAL(10,2)) AS TicketPromedio
    FROM (
        SELECT
            (SELECT COUNT(*) FROM C)                                   AS CitasTotal,
            (SELECT COUNT(*) FROM C WHERE Estado = 'Pendiente')        AS Pendientes,
            (SELECT COUNT(*) FROM C WHERE Estado = 'Confirmada')       AS Confirmadas,
            (SELECT COUNT(*) FROM C WHERE Estado = 'Completada')       AS Completadas,
            (SELECT COUNT(*) FROM C WHERE Estado = 'Cancelada')        AS Canceladas,
            (SELECT ISNULL(SUM(p.Monto), 0)
               FROM dbo.Pagos p INNER JOIN C ON C.CitaID = p.CitaID
              WHERE p.EstadoPago = 'Completado')                       AS Ingresos,
            (SELECT ISNULL(SUM(g.Monto), 0)
               FROM dbo.Gastos g
              WHERE g.SucursalID = @SucursalID AND g.Fecha >= @Ini AND g.Fecha < @Fin) AS Gastos,
            (SELECT COUNT(DISTINCT COALESCE('u' + CAST(UsuarioID AS VARCHAR(20)),
                                            'e' + ISNULL(TelefonoExterno, NombreClienteExterno)))
               FROM C WHERE Estado <> 'Cancelada')                     AS ClientesUnicos,
            (SELECT CAST(AVG(CAST(r.Calificacion AS DECIMAL(4,2))) AS DECIMAL(4,2))
               FROM dbo.Resenas r INNER JOIN C ON C.CitaID = r.CitaID) AS CalificacionPromedio,
            (SELECT COUNT(*) FROM dbo.Resenas r INNER JOIN C ON C.CitaID = r.CitaID) AS TotalResenas
    ) x;
END;
GO

-- ---------------------------------------------------------------------
-- 2. Agenda detallada (cliente real, teléfono, servicios, total, pago)
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_AgendaAdmin
    @SucursalID INT,
    @Desde DATE,
    @Hasta DATE,
    @Estado VARCHAR(30) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Ini DATETIME = CAST(@Desde AS DATETIME);
    DECLARE @Fin DATETIME = DATEADD(DAY, 1, CAST(@Hasta AS DATETIME));

    SELECT
        c.CitaID,
        c.FechaHora,
        c.Estado,
        COALESCE(u.Nombre, c.NombreClienteExterno, 'Cliente de paso') AS Cliente,
        COALESCE(c.TelefonoExterno, u.Telefono)                      AS Telefono,
        CAST(CASE WHEN c.UsuarioID IS NULL THEN 0 ELSE 1 END AS BIT)   AS TieneCuenta,
        ue.Nombre                                                     AS Barbero,
        ISNULL(sv.Servicios, '')                                      AS Servicios,
        ISNULL(sv.Total, 0)                                           AS Total,
        ISNULL(sv.Minutos, 0)                                         AS Minutos,
        pg.MetodoPago,
        pg.EstadoPago
    FROM dbo.Citas c
    INNER JOIN dbo.Empleados e  ON e.EmpleadoID = c.EmpleadoID
    INNER JOIN dbo.Usuarios ue  ON ue.UsuarioID = e.UsuarioID
    LEFT  JOIN dbo.Usuarios u   ON u.UsuarioID = c.UsuarioID
    OUTER APPLY (
        SELECT STRING_AGG(co.Nombre, ', ') AS Servicios,
               SUM(co.Precio)              AS Total,
               SUM(co.DuracionMinutos)     AS Minutos
        FROM dbo.CitaCortes cc
        INNER JOIN dbo.Cortes co ON co.CorteID = cc.CorteID
        WHERE cc.CitaID = c.CitaID
    ) sv
    OUTER APPLY (
        SELECT TOP (1) p.MetodoPago, p.EstadoPago
        FROM dbo.Pagos p WHERE p.CitaID = c.CitaID ORDER BY p.PagoID DESC
    ) pg
    WHERE c.SucursalID = @SucursalID
      AND c.FechaHora >= @Ini AND c.FechaHora < @Fin
      AND (@Estado IS NULL OR c.Estado = @Estado)
    ORDER BY
        CASE WHEN c.Estado IN ('Pendiente', 'Confirmada') THEN 0 ELSE 1 END,
        CASE WHEN c.Estado IN ('Pendiente', 'Confirmada') THEN c.FechaHora END ASC,
        c.FechaHora DESC;
END;
GO

-- ---------------------------------------------------------------------
-- 3. Cambiar el estado de una cita (transaccional)
--    - Completada: registra o completa el pago con el total de los servicios.
--    - Cancelada: cancela el pago pendiente.
--    - Registra el aviso en Notificaciones (clientes con cuenta) y devuelve
--      los datos para enviar el mensaje por Telegram desde la aplicación.
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_CambiarEstadoCita
    @CitaID INT,
    @Estado VARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @Actual VARCHAR(30), @Suc INT, @Usr INT, @Fecha DATETIME;
        SELECT @Actual = Estado, @Suc = SucursalID, @Usr = UsuarioID, @Fecha = FechaHora
        FROM dbo.Citas WITH (UPDLOCK, ROWLOCK)
        WHERE CitaID = @CitaID;

        IF @Suc IS NULL
            RAISERROR('La cita no existe.', 16, 1);
        IF @Estado NOT IN ('Pendiente', 'Confirmada', 'Completada', 'Cancelada')
            RAISERROR('Estado no válido.', 16, 1);
        IF @Actual IN ('Completada', 'Cancelada')
            RAISERROR('La cita ya está finalizada y no se puede modificar.', 16, 1);

        UPDATE dbo.Citas SET Estado = @Estado WHERE CitaID = @CitaID;

        DECLARE @Total DECIMAL(10,2) = (
            SELECT ISNULL(SUM(co.Precio), 0)
            FROM dbo.CitaCortes cc INNER JOIN dbo.Cortes co ON co.CorteID = cc.CorteID
            WHERE cc.CitaID = @CitaID);
        DECLARE @PagoID INT = (SELECT TOP (1) PagoID FROM dbo.Pagos WHERE CitaID = @CitaID ORDER BY PagoID DESC);

        IF @Estado = 'Completada'
        BEGIN
            IF @PagoID IS NULL
                INSERT INTO dbo.Pagos (CitaID, Monto, MetodoPago, FechaPago, EstadoPago)
                VALUES (@CitaID, @Total, 'Efectivo (Pago en sucursal)', dbo.fn_AhoraMexico(), 'Completado');
            ELSE
                UPDATE dbo.Pagos
                SET Monto = CASE WHEN @Total > 0 THEN @Total ELSE Monto END,
                    EstadoPago = 'Completado',
                    FechaPago = dbo.fn_AhoraMexico()
                WHERE PagoID = @PagoID;
        END
        ELSE IF @Estado = 'Cancelada' AND @PagoID IS NOT NULL
        BEGIN
            UPDATE dbo.Pagos SET EstadoPago = 'Cancelado'
            WHERE PagoID = @PagoID AND EstadoPago = 'Pendiente';
        END

        DECLARE @Sucursal VARCHAR(100) = (SELECT Nombre FROM dbo.Sucursales WHERE SucursalID = @Suc);
        DECLARE @Cuando VARCHAR(20) = FORMAT(@Fecha, 'dd/MM/yyyy HH:mm');
        DECLARE @Msg VARCHAR(255) = CASE @Estado
            WHEN 'Confirmada' THEN 'Tu cita del ' + @Cuando + ' en sucursal ' + @Sucursal + ' fue confirmada. ¡Te esperamos!'
            WHEN 'Cancelada'  THEN 'Tu cita del ' + @Cuando + ' en sucursal ' + @Sucursal + ' fue cancelada. Puedes reservar otro horario en la web.'
            WHEN 'Completada' THEN '¡Gracias por tu visita! Califica tu servicio del ' + @Cuando + ' en la sección Reseñas.'
        END;

        IF @Usr IS NOT NULL AND @Msg IS NOT NULL
            INSERT INTO dbo.Notificaciones (UsuarioID, Mensaje, Tipo, FechaEnvio, Leido)
            VALUES (@Usr, @Msg, 'Telegram', dbo.fn_AhoraMexico(), 0);

        COMMIT TRANSACTION;

        SELECT @Suc AS SucursalID, u.TelegramChatId, u.Nombre AS ClienteNombre, @Msg AS Mensaje
        FROM (SELECT 1 AS x) d
        LEFT JOIN dbo.Usuarios u ON u.UsuarioID = @Usr;
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

-- ---------------------------------------------------------------------
-- 4. Rendimiento por barbero en el periodo
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_RendimientoBarberos
    @SucursalID INT,
    @Desde DATE,
    @Hasta DATE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Ini DATETIME = CAST(@Desde AS DATETIME);
    DECLARE @Fin DATETIME = DATEADD(DAY, 1, CAST(@Hasta AS DATETIME));

    SELECT
        e.EmpleadoID,
        u.Nombre,
        CAST(ISNULL(e.Estado, 1) AS BIT)                              AS Activo,
        COUNT(c.CitaID)                                               AS Citas,
        SUM(CASE WHEN c.Estado = 'Completada' THEN 1 ELSE 0 END)      AS Completadas,
        SUM(CASE WHEN c.Estado = 'Cancelada'  THEN 1 ELSE 0 END)      AS Canceladas,
        ISNULL((SELECT SUM(p.Monto)
                  FROM dbo.Pagos p INNER JOIN dbo.Citas c2 ON c2.CitaID = p.CitaID
                 WHERE c2.EmpleadoID = e.EmpleadoID AND p.EstadoPago = 'Completado'
                   AND c2.FechaHora >= @Ini AND c2.FechaHora < @Fin), 0) AS Ingresos,
        (SELECT CAST(AVG(CAST(r.Calificacion AS DECIMAL(4,2))) AS DECIMAL(4,2))
           FROM dbo.Resenas r INNER JOIN dbo.Citas c3 ON c3.CitaID = r.CitaID
          WHERE c3.EmpleadoID = e.EmpleadoID)                         AS Calificacion,
        (SELECT COUNT(*)
           FROM dbo.Resenas r INNER JOIN dbo.Citas c3 ON c3.CitaID = r.CitaID
          WHERE c3.EmpleadoID = e.EmpleadoID)                         AS Resenas
    FROM dbo.Empleados e
    INNER JOIN dbo.Usuarios u ON u.UsuarioID = e.UsuarioID
    LEFT JOIN dbo.Citas c ON c.EmpleadoID = e.EmpleadoID AND c.FechaHora >= @Ini AND c.FechaHora < @Fin
    WHERE e.SucursalID = @SucursalID
    GROUP BY e.EmpleadoID, u.Nombre, e.Estado
    ORDER BY Ingresos DESC, Completadas DESC, u.Nombre;
END;
GO

-- ---------------------------------------------------------------------
-- 5. Servicios más vendidos (citas no canceladas)
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_ServiciosMasVendidos
    @SucursalID INT,
    @Desde DATE,
    @Hasta DATE,
    @Top INT = 5
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Ini DATETIME = CAST(@Desde AS DATETIME);
    DECLARE @Fin DATETIME = DATEADD(DAY, 1, CAST(@Hasta AS DATETIME));

    SELECT TOP (@Top)
        co.CorteID,
        co.Nombre,
        COUNT(*)        AS Veces,
        SUM(co.Precio)  AS Monto
    FROM dbo.CitaCortes cc
    INNER JOIN dbo.Citas c   ON c.CitaID = cc.CitaID
    INNER JOIN dbo.Cortes co ON co.CorteID = cc.CorteID
    WHERE c.SucursalID = @SucursalID
      AND c.FechaHora >= @Ini AND c.FechaHora < @Fin
      AND c.Estado <> 'Cancelada'
    GROUP BY co.CorteID, co.Nombre
    ORDER BY Veces DESC, Monto DESC;
END;
GO

-- ---------------------------------------------------------------------
-- 6. Ingresos por día (para la gráfica)
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_IngresosPorDia
    @SucursalID INT,
    @Desde DATE,
    @Hasta DATE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Ini DATETIME = CAST(@Desde AS DATETIME);
    DECLARE @Fin DATETIME = DATEADD(DAY, 1, CAST(@Hasta AS DATETIME));

    SELECT
        CAST(c.FechaHora AS DATE)  AS Dia,
        SUM(p.Monto)               AS Ingresos,
        COUNT(DISTINCT c.CitaID)   AS Citas
    FROM dbo.Pagos p
    INNER JOIN dbo.Citas c ON c.CitaID = p.CitaID
    WHERE c.SucursalID = @SucursalID
      AND c.FechaHora >= @Ini AND c.FechaHora < @Fin
      AND p.EstadoPago = 'Completado'
    GROUP BY CAST(c.FechaHora AS DATE)
    ORDER BY Dia;
END;
GO

-- ---------------------------------------------------------------------
-- 7. Gastos: registrar / eliminar  (@Opcion 1 = insertar, 2 = eliminar)
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_GestionarGasto
    @Opcion INT,
    @GastoID INT = NULL,
    @SucursalID INT = NULL,
    @Concepto VARCHAR(150) = NULL,
    @Monto DECIMAL(10,2) = NULL,
    @Fecha DATETIME = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Opcion = 1
    BEGIN
        IF @SucursalID IS NULL OR NOT EXISTS (SELECT 1 FROM dbo.Sucursales WHERE SucursalID = @SucursalID)
            RAISERROR('La sucursal no es válida.', 16, 1);
        ELSE IF LEN(LTRIM(RTRIM(ISNULL(@Concepto, '')))) < 3
            RAISERROR('Escribe un concepto de al menos 3 caracteres.', 16, 1);
        ELSE IF @Monto IS NULL OR @Monto <= 0
            RAISERROR('El monto debe ser mayor a 0.', 16, 1);
        ELSE
        BEGIN
            INSERT INTO dbo.Gastos (SucursalID, Concepto, Monto, Fecha)
            VALUES (@SucursalID, LTRIM(RTRIM(@Concepto)), @Monto, ISNULL(@Fecha, dbo.fn_AhoraMexico()));
            SELECT CAST(SCOPE_IDENTITY() AS INT) AS GastoID;
        END
    END
    ELSE IF @Opcion = 2
    BEGIN
        DELETE FROM dbo.Gastos WHERE GastoID = @GastoID;
        IF @@ROWCOUNT = 0 RAISERROR('El gasto no existe.', 16, 1);
    END
END;
GO

-- ---------------------------------------------------------------------
-- 8. Gastos de una sucursal en el periodo
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_ObtenerGastos
    @SucursalID INT,
    @Desde DATE,
    @Hasta DATE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Ini DATETIME = CAST(@Desde AS DATETIME);
    DECLARE @Fin DATETIME = DATEADD(DAY, 1, CAST(@Hasta AS DATETIME));

    SELECT GastoID, Fecha, Concepto, Monto
    FROM dbo.Gastos
    WHERE SucursalID = @SucursalID AND Fecha >= @Ini AND Fecha < @Fin
    ORDER BY Fecha DESC, GastoID DESC;
END;
GO

-- ---------------------------------------------------------------------
-- 9. Activar / desactivar un servicio del catálogo
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_CambiarEstadoCorte
    @CorteID INT,
    @Estado BIT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Cortes SET Estado = @Estado WHERE CorteID = @CorteID;
    IF @@ROWCOUNT = 0 RAISERROR('El servicio no existe.', 16, 1);
END;
GO

-- ---------------------------------------------------------------------
-- 10. Activar / desactivar un barbero.
--     Devuelve cuántas citas activas futuras tiene, para avisar al admin.
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_CambiarEstadoEmpleado
    @EmpleadoID INT,
    @Estado BIT
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Empleados SET Estado = @Estado WHERE EmpleadoID = @EmpleadoID;
    IF @@ROWCOUNT = 0
    BEGIN
        RAISERROR('El barbero no existe.', 16, 1);
        RETURN;
    END

    SELECT COUNT(*) AS CitasProximas
    FROM dbo.Citas
    WHERE EmpleadoID = @EmpleadoID
      AND FechaHora >= dbo.fn_AhoraMexico()
      AND Estado IN ('Pendiente', 'Confirmada');
END;
GO

-- ---------------------------------------------------------------------
-- 11. Personal (versión ampliada; conserva las columnas originales)
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_ObtenerBarberosAdmin
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        e.EmpleadoId,
        u.Nombre AS NombreBarbero,
        u.Correo,
        s.Nombre AS SucursalAsignada,
        e.Especialidad,
        e.HorarioInicio,
        e.HorarioFin,
        CAST(ISNULL(e.Estado, 1) AS BIT) AS Activo,
        (SELECT COUNT(*) FROM dbo.Citas c
          WHERE c.EmpleadoID = e.EmpleadoID AND c.FechaHora >= dbo.fn_AhoraMexico()
            AND c.Estado IN ('Pendiente', 'Confirmada')) AS CitasProximas,
        (SELECT COUNT(*) FROM dbo.Citas c
          WHERE c.EmpleadoID = e.EmpleadoID AND c.Estado = 'Completada') AS CitasCompletadas
    FROM dbo.Empleados e
    INNER JOIN dbo.Usuarios u ON e.UsuarioId = u.UsuarioId
    INNER JOIN dbo.Sucursales s ON e.SucursalId = s.SucursalId
    ORDER BY s.SucursalID, u.Nombre;
END;
GO

-- #####################################################################
-- sp_PersonalYResumen.sql
-- #####################################################################
-- =====================================================================
-- LUMINA RESERVE - PERSONAL (alta/edición de barberos) Y RESUMEN DIARIO
-- Ejecutar sobre LuminaReserveDB (CREATE OR ALTER: se puede re-ejecutar).
-- Desde sqlcmd usar -f 65001 para que los acentos se guarden bien.
-- =====================================================================

-- ---------------------------------------------------------------------
-- sp_GestionarEmpleado (ampliado; la opción 1 conserva su comportamiento original)
--   @Opcion 1: Insertar empleado para un usuario existente (@UsuarioID)
--   @Opcion 2: Actualizar barbero (@EmpleadoID): sucursal, especialidad, horario,
--              y nombre / teléfono de su usuario
--   @Opcion 3: Alta completa: crea el usuario (si el correo no existe) y el empleado.
--              Si el correo ya existe y no es barbero, reutiliza ese usuario.
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_GestionarEmpleado
    @Opcion INT,
    @UsuarioID INT = NULL,
    @SucursalID INT = 1,
    @Especialidad VARCHAR(100) = NULL,
    @HorarioInicio TIME = '10:00:00',
    @HorarioFin TIME = '20:00:00',
    @Estado BIT = 1,
    @EmpleadoID INT = NULL,
    @Nombre VARCHAR(100) = NULL,
    @Correo VARCHAR(100) = NULL,
    @Telefono VARCHAR(20) = NULL,
    @PasswordHash VARCHAR(256) = NULL
AS
BEGIN
    SET NOCOUNT ON;

    IF @Opcion = 1
    BEGIN
        INSERT INTO dbo.Empleados (UsuarioID, SucursalID, Especialidad, HorarioInicio, HorarioFin, Estado)
        VALUES (@UsuarioID, @SucursalID, @Especialidad, @HorarioInicio, @HorarioFin, @Estado);

        SELECT SCOPE_IDENTITY() AS NuevoEmpleadoID;
        RETURN;
    END

    BEGIN TRY
        BEGIN TRANSACTION;

        IF NOT EXISTS (SELECT 1 FROM dbo.Sucursales WHERE SucursalID = @SucursalID)
            RAISERROR('La sucursal seleccionada no es válida.', 16, 1);
        IF @HorarioInicio IS NULL OR @HorarioFin IS NULL OR @HorarioInicio >= @HorarioFin
            RAISERROR('El horario de salida debe ser posterior al de entrada.', 16, 1);

        IF @Opcion = 2
        BEGIN
            DECLARE @Usr INT = (SELECT UsuarioID FROM dbo.Empleados WHERE EmpleadoID = @EmpleadoID);
            IF @Usr IS NULL
                RAISERROR('El barbero no existe.', 16, 1);
            IF LEN(LTRIM(RTRIM(ISNULL(@Nombre, '')))) < 2
                RAISERROR('Escribe el nombre del barbero.', 16, 1);

            UPDATE dbo.Empleados
            SET SucursalID = @SucursalID,
                Especialidad = NULLIF(LTRIM(RTRIM(@Especialidad)), ''),
                HorarioInicio = @HorarioInicio,
                HorarioFin = @HorarioFin
            WHERE EmpleadoID = @EmpleadoID;

            UPDATE dbo.Usuarios
            SET Nombre = LTRIM(RTRIM(@Nombre)),
                Telefono = NULLIF(LTRIM(RTRIM(@Telefono)), '')
            WHERE UsuarioID = @Usr;

            COMMIT TRANSACTION;
            SELECT @EmpleadoID AS EmpleadoID, CAST(0 AS BIT) AS UsuarioCreado;
            RETURN;
        END

        IF @Opcion = 3
        BEGIN
            SET @Correo = LOWER(LTRIM(RTRIM(ISNULL(@Correo, ''))));
            IF LEN(LTRIM(RTRIM(ISNULL(@Nombre, '')))) < 2
                RAISERROR('Escribe el nombre del barbero.', 16, 1);
            IF @Correo NOT LIKE '%_@_%._%'
                RAISERROR('Escribe un correo válido.', 16, 1);

            DECLARE @UsuarioExistente INT = (SELECT UsuarioID FROM dbo.Usuarios WHERE LOWER(Correo) = @Correo);
            DECLARE @Creado BIT = 0;

            IF @UsuarioExistente IS NOT NULL
            BEGIN
                IF EXISTS (SELECT 1 FROM dbo.Empleados WHERE UsuarioID = @UsuarioExistente)
                    RAISERROR('Ese correo ya pertenece a un barbero registrado.', 16, 1);
            END
            ELSE
            BEGIN
                IF @PasswordHash IS NULL OR LEN(@PasswordHash) = 0
                    RAISERROR('Asigna una contraseña inicial para el nuevo usuario.', 16, 1);

                INSERT INTO dbo.Usuarios (Nombre, Correo, PasswordHash, Telefono, FechaRegistro, Estado)
                VALUES (LTRIM(RTRIM(@Nombre)), @Correo, @PasswordHash, NULLIF(LTRIM(RTRIM(@Telefono)), ''), dbo.fn_AhoraMexico(), 1);
                SET @UsuarioExistente = CAST(SCOPE_IDENTITY() AS INT);
                SET @Creado = 1;
            END

            INSERT INTO dbo.Empleados (UsuarioID, SucursalID, Especialidad, HorarioInicio, HorarioFin, Estado)
            VALUES (@UsuarioExistente, @SucursalID, NULLIF(LTRIM(RTRIM(@Especialidad)), ''), @HorarioInicio, @HorarioFin, 1);

            DECLARE @Nuevo INT = CAST(SCOPE_IDENTITY() AS INT);
            COMMIT TRANSACTION;
            SELECT @Nuevo AS EmpleadoID, @Creado AS UsuarioCreado;
            RETURN;
        END

        RAISERROR('Opción no válida.', 16, 1);
    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH
END;
GO

-- ---------------------------------------------------------------------
-- sp_ObtenerBarberosAdmin (agrega SucursalID y Telefono para el formulario de edición)
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_ObtenerBarberosAdmin
AS
BEGIN
    SET NOCOUNT ON;
    SELECT
        e.EmpleadoId,
        u.Nombre AS NombreBarbero,
        u.Correo,
        s.Nombre AS SucursalAsignada,
        e.Especialidad,
        e.HorarioInicio,
        e.HorarioFin,
        CAST(ISNULL(e.Estado, 1) AS BIT) AS Activo,
        (SELECT COUNT(*) FROM dbo.Citas c
          WHERE c.EmpleadoID = e.EmpleadoID AND c.FechaHora >= dbo.fn_AhoraMexico()
            AND c.Estado IN ('Pendiente', 'Confirmada')) AS CitasProximas,
        (SELECT COUNT(*) FROM dbo.Citas c
          WHERE c.EmpleadoID = e.EmpleadoID AND c.Estado = 'Completada') AS CitasCompletadas,
        e.SucursalID,
        u.Telefono
    FROM dbo.Empleados e
    INNER JOIN dbo.Usuarios u ON e.UsuarioId = u.UsuarioId
    INNER JOIN dbo.Sucursales s ON e.SucursalId = s.SucursalId
    ORDER BY s.SucursalID, u.Nombre;
END;
GO

-- ---------------------------------------------------------------------
-- sp_ResumenDiarioAdmin: datos del resumen diario por Telegram
--   Resultado 1: por sucursal -> citas de hoy, por atender, ingresos/gastos de ayer
--   Resultado 2: detalle de las citas de hoy (no canceladas)
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_ResumenDiarioAdmin
    @Fecha DATE
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Hoy DATETIME = CAST(@Fecha AS DATETIME);
    DECLARE @Manana DATETIME = DATEADD(DAY, 1, @Hoy);
    DECLARE @Ayer DATETIME = DATEADD(DAY, -1, @Hoy);

    SELECT
        s.SucursalID,
        s.Nombre,
        (SELECT COUNT(*) FROM dbo.Citas c
          WHERE c.SucursalID = s.SucursalID AND c.FechaHora >= @Hoy AND c.FechaHora < @Manana
            AND c.Estado <> 'Cancelada')                                   AS CitasHoy,
        (SELECT COUNT(*) FROM dbo.Citas c
          WHERE c.SucursalID = s.SucursalID AND c.FechaHora >= @Hoy AND c.FechaHora < @Manana
            AND c.Estado IN ('Pendiente', 'Confirmada'))                   AS PorAtenderHoy,
        (SELECT ISNULL(SUM(p.Monto), 0) FROM dbo.Pagos p
          INNER JOIN dbo.Citas c ON c.CitaID = p.CitaID
          WHERE c.SucursalID = s.SucursalID AND c.FechaHora >= @Ayer AND c.FechaHora < @Hoy
            AND p.EstadoPago = 'Completado')                               AS IngresosAyer,
        (SELECT COUNT(*) FROM dbo.Citas c
          WHERE c.SucursalID = s.SucursalID AND c.FechaHora >= @Ayer AND c.FechaHora < @Hoy
            AND c.Estado = 'Completada')                                   AS CompletadasAyer,
        (SELECT COUNT(*) FROM dbo.Citas c
          WHERE c.SucursalID = s.SucursalID AND c.FechaHora >= @Ayer AND c.FechaHora < @Hoy
            AND c.Estado = 'Cancelada')                                    AS CanceladasAyer,
        (SELECT ISNULL(SUM(g.Monto), 0) FROM dbo.Gastos g
          WHERE g.SucursalID = s.SucursalID AND g.Fecha >= @Ayer AND g.Fecha < @Hoy) AS GastosAyer
    FROM dbo.Sucursales s
    WHERE ISNULL(s.Estado, 1) = 1
    ORDER BY s.SucursalID;

    SELECT
        s.Nombre                                                       AS Sucursal,
        c.FechaHora,
        c.Estado,
        COALESCE(u.Nombre, c.NombreClienteExterno, 'Cliente de paso')  AS Cliente,
        ue.Nombre                                                      AS Barbero,
        ISNULL((SELECT STRING_AGG(co.Nombre, ', ')
                  FROM dbo.CitaCortes cc INNER JOIN dbo.Cortes co ON co.CorteID = cc.CorteID
                 WHERE cc.CitaID = c.CitaID), '')                      AS Servicios
    FROM dbo.Citas c
    INNER JOIN dbo.Sucursales s ON s.SucursalID = c.SucursalID
    INNER JOIN dbo.Empleados e  ON e.EmpleadoID = c.EmpleadoID
    INNER JOIN dbo.Usuarios ue  ON ue.UsuarioID = e.UsuarioID
    LEFT  JOIN dbo.Usuarios u   ON u.UsuarioID = c.UsuarioID
    WHERE c.FechaHora >= @Hoy AND c.FechaHora < @Manana AND c.Estado <> 'Cancelada'
    ORDER BY s.SucursalID, c.FechaHora;
END;
GO

-- ---------------------------------------------------------------------
-- sp_ControlResumenDiario: evita enviar el resumen dos veces el mismo día
--   @Opcion 1: ¿ya se envió el resumen de @Fecha?  -> YaEnviado (bit)
--   @Opcion 2: registra el envío en Notificaciones (Tipo = 'ResumenDiario')
--              a nombre del administrador (el que tenga @ChatId, o el primero)
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_ControlResumenDiario
    @Opcion INT,
    @Fecha DATE,
    @ChatId BIGINT = NULL,
    @Mensaje VARCHAR(255) = NULL
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @Ini DATETIME = CAST(@Fecha AS DATETIME);
    DECLARE @Fin DATETIME = DATEADD(DAY, 1, @Ini);

    IF @Opcion = 1
    BEGIN
        SELECT CAST(CASE WHEN EXISTS (
                   SELECT 1 FROM dbo.Notificaciones
                   WHERE Tipo = 'ResumenDiario' AND FechaEnvio >= @Ini AND FechaEnvio < @Fin)
               THEN 1 ELSE 0 END AS BIT) AS YaEnviado;
        RETURN;
    END

    IF @Opcion = 2
    BEGIN
        DECLARE @Admin INT = (
            SELECT TOP (1) u.UsuarioID
            FROM dbo.Usuarios u
            INNER JOIN dbo.UsuarioRoles ur ON ur.UsuarioID = u.UsuarioID
            INNER JOIN dbo.Roles r ON r.RolID = ur.RolID
            WHERE r.Nombre IN ('Admin', 'Administrador')
            ORDER BY CASE WHEN u.TelegramChatId = @ChatId THEN 0 ELSE 1 END, u.UsuarioID);

        IF @Admin IS NULL
        BEGIN
            RAISERROR('No hay un usuario administrador para registrar el resumen.', 16, 1);
            RETURN;
        END

        INSERT INTO dbo.Notificaciones (UsuarioID, Mensaje, Tipo, FechaEnvio, Leido)
        VALUES (@Admin, LEFT(ISNULL(@Mensaje, 'Resumen diario enviado'), 255), 'ResumenDiario', dbo.fn_AhoraMexico(), 1);
    END
END;
GO

-- #####################################################################
-- sp_RecuperarPassword.sql
-- #####################################################################
-- =====================================================================
-- LUMINA RESERVE - RECUPERACIÓN DE CONTRASEÑA POR TELEGRAM
-- Ejecutar sobre LuminaReserveDB (CREATE OR ALTER: se puede re-ejecutar).
-- Desde sqlcmd usar -f 65001 para que los acentos se guarden bien.
--
-- Los códigos se guardan en CodigosVerificacionBot con prefijo 'R'
-- (ej. 'R482913') para no mezclarse con los códigos de inicio de sesión.
-- El código de 6 dígitos y el hash de la contraseña se generan en la aplicación.
-- ---------------------------------------------------------------------
--   @Opcion 1: Solicitar código. Devuelve Enviar = 1 y el TelegramChatId si
--              la cuenta existe, está activa y tiene Telegram vinculado.
--              Motivo: 'OK' | 'SinCuenta' | 'SinTelegram' | 'SinVerificar' | 'Reciente'
--   @Opcion 2: Restablecer. Valida el código y reemplaza PasswordHash
--              en una transacción; el código queda marcado como usado.
-- =====================================================================
CREATE OR ALTER PROCEDURE dbo.sp_RecuperarPassword
    @Opcion INT,
    @Correo VARCHAR(100),
    @Codigo VARCHAR(9) = NULL,
    @PasswordHash VARCHAR(256) = NULL,
    @MinutosVigencia INT = 10
AS
BEGIN
    SET NOCOUNT ON;
    SET @Correo = LOWER(LTRIM(RTRIM(ISNULL(@Correo, ''))));

    DECLARE @Usr INT, @Chat BIGINT, @Nombre VARCHAR(100);
    SELECT @Usr = UsuarioID, @Chat = TelegramChatId, @Nombre = Nombre
    FROM dbo.Usuarios
    WHERE LOWER(Correo) = @Correo AND ISNULL(Estado, 1) = 1;

    IF @Opcion = 1
    BEGIN
        IF @Usr IS NULL
        BEGIN
            SELECT CAST(0 AS BIT) AS Enviar, CAST(NULL AS BIGINT) AS TelegramChatId, CAST(NULL AS VARCHAR(100)) AS Nombre, 'SinCuenta' AS Motivo;
            RETURN;
        END
        IF @Chat IS NULL OR @Chat = 0
        BEGIN
            SELECT CAST(0 AS BIT) AS Enviar, CAST(NULL AS BIGINT) AS TelegramChatId, @Nombre AS Nombre, 'SinTelegram' AS Motivo;
            RETURN;
        END
        -- Seguridad: solo se envía a un Telegram que ya se verificó con un inicio de sesión
        -- completo (contraseña + código). Evita que alguien vincule su propio Telegram a
        -- una cuenta ajena desde el bot y luego cambie la contraseña.
        IF NOT EXISTS (SELECT 1 FROM dbo.Notificaciones WHERE UsuarioID = @Usr AND Tipo = 'TelegramVerificado')
        BEGIN
            SELECT CAST(0 AS BIT) AS Enviar, CAST(NULL AS BIGINT) AS TelegramChatId, @Nombre AS Nombre, 'SinVerificar' AS Motivo;
            RETURN;
        END
        -- Anti-abuso: un código por minuto como máximo.
        IF EXISTS (SELECT 1 FROM dbo.CodigosVerificacionBot
                   WHERE UsuarioID = @Usr AND Codigo LIKE 'R%' AND ISNULL(Usado, 0) = 0
                     AND FechaExpiracion > DATEADD(MINUTE, @MinutosVigencia - 1, dbo.fn_AhoraMexico()))
        BEGIN
            SELECT CAST(0 AS BIT) AS Enviar, @Chat AS TelegramChatId, @Nombre AS Nombre, 'Reciente' AS Motivo;
            RETURN;
        END

        BEGIN TRY
            BEGIN TRANSACTION;
            UPDATE dbo.CodigosVerificacionBot SET Usado = 1
            WHERE UsuarioID = @Usr AND Codigo LIKE 'R%' AND ISNULL(Usado, 0) = 0;

            INSERT INTO dbo.CodigosVerificacionBot (UsuarioID, Codigo, FechaExpiracion, Usado)
            VALUES (@Usr, 'R' + @Codigo, DATEADD(MINUTE, @MinutosVigencia, dbo.fn_AhoraMexico()), 0);
            COMMIT TRANSACTION;
        END TRY
        BEGIN CATCH
            IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
            THROW;
        END CATCH

        SELECT CAST(1 AS BIT) AS Enviar, @Chat AS TelegramChatId, @Nombre AS Nombre, 'OK' AS Motivo;
        RETURN;
    END

    IF @Opcion = 2
    BEGIN
        BEGIN TRY
            BEGIN TRANSACTION;

            DECLARE @CodigoID INT = (
                SELECT TOP (1) CodigoID FROM dbo.CodigosVerificacionBot WITH (UPDLOCK, ROWLOCK)
                WHERE UsuarioID = @Usr AND Codigo = 'R' + ISNULL(@Codigo, '')
                  AND ISNULL(Usado, 0) = 0 AND FechaExpiracion >= dbo.fn_AhoraMexico()
                ORDER BY FechaExpiracion DESC);

            IF @Usr IS NULL OR @CodigoID IS NULL
                RAISERROR('El código es inválido o ya expiró. Solicita uno nuevo.', 16, 1);
            IF @PasswordHash IS NULL OR LEN(@PasswordHash) = 0
                RAISERROR('La nueva contraseña no es válida.', 16, 1);

            UPDATE dbo.CodigosVerificacionBot SET Usado = 1
            WHERE UsuarioID = @Usr AND Codigo LIKE 'R%' AND ISNULL(Usado, 0) = 0;

            UPDATE dbo.Usuarios SET PasswordHash = @PasswordHash WHERE UsuarioID = @Usr;

            COMMIT TRANSACTION;
            SELECT @Chat AS TelegramChatId, @Nombre AS Nombre;
        END TRY
        BEGIN CATCH
            IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
            THROW;
        END CATCH
    END
END;
GO

-- ---------------------------------------------------------------------
-- sp_MarcarTelegramVerificado: se llama tras un inicio de sesión exitoso
-- (contraseña + código del bot). Registra una sola vez que el Telegram
-- vinculado pertenece de verdad al dueño de la cuenta.
-- ---------------------------------------------------------------------
CREATE OR ALTER PROCEDURE dbo.sp_MarcarTelegramVerificado
    @UsuarioID INT
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM dbo.Usuarios WHERE UsuarioID = @UsuarioID AND TelegramChatId IS NOT NULL)
       AND NOT EXISTS (SELECT 1 FROM dbo.Notificaciones WHERE UsuarioID = @UsuarioID AND Tipo = 'TelegramVerificado')
    BEGIN
        INSERT INTO dbo.Notificaciones (UsuarioID, Mensaje, Tipo, FechaEnvio, Leido)
        VALUES (@UsuarioID, 'Telegram verificado con inicio de sesión', 'TelegramVerificado', dbo.fn_AhoraMexico(), 1);
    END
END;
GO
