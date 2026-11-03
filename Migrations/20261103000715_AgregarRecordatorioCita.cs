using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lumina_Reserve_2.Migrations
{
    /// <inheritdoc />
    public partial class AgregarRecordatorioCita : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Cortes",
                columns: table => new
                {
                    CorteID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Descripcion = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: true),
                    Precio = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    DuracionMinutos = table.Column<int>(type: "int", nullable: false, defaultValue: 30),
                    Estado = table.Column<bool>(type: "bit", nullable: true, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Cortes__905078314B0C09FD", x => x.CorteID);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    RolID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Roles__F92302D182645950", x => x.RolID);
                });

            migrationBuilder.CreateTable(
                name: "Sucursales",
                columns: table => new
                {
                    SucursalID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Direccion = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    Telefono = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    Estado = table.Column<bool>(type: "bit", nullable: true, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Sucursal__6CB48281D843AF6A", x => x.SucursalID);
                });

            migrationBuilder.CreateTable(
                name: "Usuarios",
                columns: table => new
                {
                    UsuarioID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Correo = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false),
                    Telefono = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    TelegramChatId = table.Column<long>(type: "bigint", nullable: true),
                    FechaRegistro = table.Column<DateTime>(type: "datetime", nullable: true, defaultValueSql: "(getdate())"),
                    Estado = table.Column<bool>(type: "bit", nullable: true, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Usuarios__2B3DE798A4C518C2", x => x.UsuarioID);
                });

            migrationBuilder.CreateTable(
                name: "Gastos",
                columns: table => new
                {
                    GastoID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SucursalID = table.Column<int>(type: "int", nullable: false),
                    Concepto = table.Column<string>(type: "varchar(150)", unicode: false, maxLength: 150, nullable: false),
                    Monto = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    Fecha = table.Column<DateTime>(type: "datetime", nullable: true, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Gastos__815BB110633EBBE5", x => x.GastoID);
                    table.ForeignKey(
                        name: "FK__Gastos__Sucursal__503BEA1C",
                        column: x => x.SucursalID,
                        principalTable: "Sucursales",
                        principalColumn: "SucursalID");
                });

            migrationBuilder.CreateTable(
                name: "CodigosVerificacionBot",
                columns: table => new
                {
                    CodigoID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioID = table.Column<int>(type: "int", nullable: false),
                    Codigo = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false),
                    FechaExpiracion = table.Column<DateTime>(type: "datetime", nullable: false),
                    Usado = table.Column<bool>(type: "bit", nullable: true, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CodigosV__DB319CF35FC0A6AE", x => x.CodigoID);
                    table.ForeignKey(
                        name: "FK__CodigosVe__Usuar__5F7E2DAC",
                        column: x => x.UsuarioID,
                        principalTable: "Usuarios",
                        principalColumn: "UsuarioID");
                });

            migrationBuilder.CreateTable(
                name: "Empleados",
                columns: table => new
                {
                    EmpleadoID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioID = table.Column<int>(type: "int", nullable: false),
                    SucursalID = table.Column<int>(type: "int", nullable: false),
                    Especialidad = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    HorarioInicio = table.Column<TimeOnly>(type: "time", nullable: true),
                    HorarioFin = table.Column<TimeOnly>(type: "time", nullable: true),
                    Estado = table.Column<bool>(type: "bit", nullable: true, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Empleado__958BE6F0500C37ED", x => x.EmpleadoID);
                    table.ForeignKey(
                        name: "FK__Empleados__Sucur__395884C4",
                        column: x => x.SucursalID,
                        principalTable: "Sucursales",
                        principalColumn: "SucursalID");
                    table.ForeignKey(
                        name: "FK__Empleados__Usuar__3864608B",
                        column: x => x.UsuarioID,
                        principalTable: "Usuarios",
                        principalColumn: "UsuarioID");
                });

            migrationBuilder.CreateTable(
                name: "Notificaciones",
                columns: table => new
                {
                    NotificacionID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioID = table.Column<int>(type: "int", nullable: false),
                    Mensaje = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    Tipo = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: true, defaultValue: "Telegram"),
                    FechaEnvio = table.Column<DateTime>(type: "datetime", nullable: true, defaultValueSql: "(getdate())"),
                    Leido = table.Column<bool>(type: "bit", nullable: true, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Notifica__BCC120C499022B05", x => x.NotificacionID);
                    table.ForeignKey(
                        name: "FK__Notificac__Usuar__5BAD9CC8",
                        column: x => x.UsuarioID,
                        principalTable: "Usuarios",
                        principalColumn: "UsuarioID");
                });

            migrationBuilder.CreateTable(
                name: "UsuarioRoles",
                columns: table => new
                {
                    UsuarioID = table.Column<int>(type: "int", nullable: false),
                    RolID = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__UsuarioR__24AFD7B5DBED7695", x => new { x.UsuarioID, x.RolID });
                    table.ForeignKey(
                        name: "FK__UsuarioRo__RolID__31B762FC",
                        column: x => x.RolID,
                        principalTable: "Roles",
                        principalColumn: "RolID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK__UsuarioRo__Usuar__30C33EC3",
                        column: x => x.UsuarioID,
                        principalTable: "Usuarios",
                        principalColumn: "UsuarioID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Citas",
                columns: table => new
                {
                    CitaID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UsuarioID = table.Column<int>(type: "int", nullable: false),
                    EmpleadoID = table.Column<int>(type: "int", nullable: false),
                    SucursalID = table.Column<int>(type: "int", nullable: false),
                    FechaHora = table.Column<DateTime>(type: "datetime", nullable: false),
                    Estado = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true, defaultValue: "Pendiente"),
                    FechaCreacion = table.Column<DateTime>(type: "datetime", nullable: true, defaultValueSql: "(getdate())"),
                    RecordatorioEnviado = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Citas__F0E2D9F222D72B55", x => x.CitaID);
                    table.ForeignKey(
                        name: "FK__Citas__EmpleadoI__42E1EEFE",
                        column: x => x.EmpleadoID,
                        principalTable: "Empleados",
                        principalColumn: "EmpleadoID");
                    table.ForeignKey(
                        name: "FK__Citas__SucursalI__43D61337",
                        column: x => x.SucursalID,
                        principalTable: "Sucursales",
                        principalColumn: "SucursalID");
                    table.ForeignKey(
                        name: "FK__Citas__UsuarioID__41EDCAC5",
                        column: x => x.UsuarioID,
                        principalTable: "Usuarios",
                        principalColumn: "UsuarioID");
                });

            migrationBuilder.CreateTable(
                name: "CitaCortes",
                columns: table => new
                {
                    CitaID = table.Column<int>(type: "int", nullable: false),
                    CorteID = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__CitaCort__F9E7DE71554BBB09", x => new { x.CitaID, x.CorteID });
                    table.ForeignKey(
                        name: "FK__CitaCorte__CitaI__46B27FE2",
                        column: x => x.CitaID,
                        principalTable: "Citas",
                        principalColumn: "CitaID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK__CitaCorte__Corte__47A6A41B",
                        column: x => x.CorteID,
                        principalTable: "Cortes",
                        principalColumn: "CorteID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Pagos",
                columns: table => new
                {
                    PagoID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CitaID = table.Column<int>(type: "int", nullable: false),
                    Monto = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    MetodoPago = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    FechaPago = table.Column<DateTime>(type: "datetime", nullable: true, defaultValueSql: "(getdate())"),
                    EstadoPago = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true, defaultValue: "Completado")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Pagos__F00B61583A6ED9B1", x => x.PagoID);
                    table.ForeignKey(
                        name: "FK__Pagos__CitaID__4C6B5938",
                        column: x => x.CitaID,
                        principalTable: "Citas",
                        principalColumn: "CitaID");
                });

            migrationBuilder.CreateTable(
                name: "Resenas",
                columns: table => new
                {
                    ResenaID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CitaID = table.Column<int>(type: "int", nullable: false),
                    UsuarioID = table.Column<int>(type: "int", nullable: false),
                    Calificacion = table.Column<int>(type: "int", nullable: true),
                    Comentario = table.Column<string>(type: "varchar(500)", unicode: false, maxLength: 500, nullable: true),
                    Fecha = table.Column<DateTime>(type: "datetime", nullable: true, defaultValueSql: "(getdate())")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Resenas__DCC1B117DEC14777", x => x.ResenaID);
                    table.ForeignKey(
                        name: "FK__Resenas__CitaID__55009F39",
                        column: x => x.CitaID,
                        principalTable: "Citas",
                        principalColumn: "CitaID");
                    table.ForeignKey(
                        name: "FK__Resenas__Usuario__55F4C372",
                        column: x => x.UsuarioID,
                        principalTable: "Usuarios",
                        principalColumn: "UsuarioID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CitaCortes_CorteID",
                table: "CitaCortes",
                column: "CorteID");

            migrationBuilder.CreateIndex(
                name: "IX_Citas_EmpleadoID",
                table: "Citas",
                column: "EmpleadoID");

            migrationBuilder.CreateIndex(
                name: "IX_Citas_SucursalID",
                table: "Citas",
                column: "SucursalID");

            migrationBuilder.CreateIndex(
                name: "IX_Citas_UsuarioID",
                table: "Citas",
                column: "UsuarioID");

            migrationBuilder.CreateIndex(
                name: "IX_CodigosVerificacionBot_UsuarioID",
                table: "CodigosVerificacionBot",
                column: "UsuarioID");

            migrationBuilder.CreateIndex(
                name: "IX_Empleados_SucursalID",
                table: "Empleados",
                column: "SucursalID");

            migrationBuilder.CreateIndex(
                name: "IX_Empleados_UsuarioID",
                table: "Empleados",
                column: "UsuarioID");

            migrationBuilder.CreateIndex(
                name: "IX_Gastos_SucursalID",
                table: "Gastos",
                column: "SucursalID");

            migrationBuilder.CreateIndex(
                name: "IX_Notificaciones_UsuarioID",
                table: "Notificaciones",
                column: "UsuarioID");

            migrationBuilder.CreateIndex(
                name: "IX_Pagos_CitaID",
                table: "Pagos",
                column: "CitaID");

            migrationBuilder.CreateIndex(
                name: "IX_Resenas_CitaID",
                table: "Resenas",
                column: "CitaID");

            migrationBuilder.CreateIndex(
                name: "IX_Resenas_UsuarioID",
                table: "Resenas",
                column: "UsuarioID");

            migrationBuilder.CreateIndex(
                name: "UQ__Roles__75E3EFCF036A6B18",
                table: "Roles",
                column: "Nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsuarioRoles_RolID",
                table: "UsuarioRoles",
                column: "RolID");

            migrationBuilder.CreateIndex(
                name: "UQ__Usuarios__60695A19999EE422",
                table: "Usuarios",
                column: "Correo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CitaCortes");

            migrationBuilder.DropTable(
                name: "CodigosVerificacionBot");

            migrationBuilder.DropTable(
                name: "Gastos");

            migrationBuilder.DropTable(
                name: "Notificaciones");

            migrationBuilder.DropTable(
                name: "Pagos");

            migrationBuilder.DropTable(
                name: "Resenas");

            migrationBuilder.DropTable(
                name: "UsuarioRoles");

            migrationBuilder.DropTable(
                name: "Cortes");

            migrationBuilder.DropTable(
                name: "Citas");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Empleados");

            migrationBuilder.DropTable(
                name: "Sucursales");

            migrationBuilder.DropTable(
                name: "Usuarios");
        }
    }
}
