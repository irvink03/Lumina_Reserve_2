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
