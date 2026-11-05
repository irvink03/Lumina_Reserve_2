using System.Globalization;
using ClosedXML.Excel;
using Lumina_Reserve_2.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Lumina_Reserve_2.Services
{
    // Genera el reporte del panel en Excel (.xlsx, ClosedXML) y PDF (QuestPDF).
    // Los datos vienen de ReportesAdminService (procedimientos almacenados).
    public class ExportadorReportes
    {
        private const string FormatoMoneda = "\"$\"#,##0.00";
        private static readonly CultureInfo Es = new("es-MX");

        public static string NombreArchivo(ReporteAdmin r, string extension) =>
            $"Reporte_LuminaReserve_{Sanitizar(r.Filtro.SucursalNombre)}_{r.Filtro.Desde:yyyyMMdd}-{r.Filtro.Hasta:yyyyMMdd}.{extension}";

        private static string Sanitizar(string t) =>
            new string(t.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray()) is { Length: > 0 } s ? s : "Sucursal";

        // ============================== EXCEL ==============================

        public byte[] GenerarExcel(ReporteAdmin r)
        {
            using var wb = new XLWorkbook();
            var res = r.Resumen;

            // ---- Hoja Resumen ----
            var ws = wb.Worksheets.Add("Resumen");
            ws.Cell(1, 1).Value = "LUMINA RESERVE · Reporte del panel";
            ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(16).Font.SetFontColor(XLColor.FromHtml("#9C7A1C"));
            ws.Cell(2, 1).Value = $"Sucursal {r.Filtro.SucursalNombre} · {r.Filtro.Etiqueta}";
            ws.Cell(3, 1).Value = $"Generado el {r.Generado:dd/MM/yyyy HH:mm}";
            ws.Range(2, 1, 3, 1).Style.Font.SetFontColor(XLColor.Gray);

            var kpis = new (string, object, bool)[]
            {
                ("Ingresos (pagos completados)", res.Ingresos, true),
                ("Gastos", res.Gastos, true),
                ("Utilidad neta", res.Utilidad, true),
                ("Ticket promedio", res.TicketPromedio, true),
                ("Citas en el periodo", res.CitasTotal, false),
                ("Pendientes", res.Pendientes, false),
                ("Confirmadas", res.Confirmadas, false),
                ("Completadas", res.Completadas, false),
                ("Canceladas", res.Canceladas, false),
                ("Clientes atendidos", res.ClientesUnicos, false),
                ("Calificación promedio", res.CalificacionPromedio.HasValue ? res.CalificacionPromedio.Value : "Sin reseñas", false),
                ("Reseñas", res.TotalResenas, false)
            };
            ws.Cell(5, 1).Value = "Indicador";
            ws.Cell(5, 2).Value = "Valor";
            for (var i = 0; i < kpis.Length; i++)
            {
                var (nombre, valor, moneda) = kpis[i];
                ws.Cell(6 + i, 1).Value = nombre;
                ws.Cell(6 + i, 2).Value = XLCellValue.FromObject(valor);
                if (moneda) ws.Cell(6 + i, 2).Style.NumberFormat.Format = FormatoMoneda;
            }
            EstiloTabla(ws.Range(5, 1, 5 + kpis.Length, 2), "Indicadores");
            ws.Columns().AdjustToContents();

            // ---- Hojas de detalle ----
            HojaTabla(wb, "Citas",
                new[] { "Fecha", "Hora", "Cliente", "Teléfono", "Con cuenta", "Barbero", "Servicios", "Minutos", "Total", "Método de pago", "Estado de pago", "Estado" },
                r.Agenda.Select(c => new object?[] { c.FechaHora.Date, c.FechaHora.ToString("HH:mm"), c.Cliente, c.Telefono, c.TieneCuenta ? "Sí" : "Invitado",
                                                     c.Barbero, c.Servicios, c.Minutos, c.Total, c.MetodoPago, c.EstadoPago, c.Estado }),
                monedaCols: new[] { 9 }, fechaCols: new[] { 1 });

            HojaTabla(wb, "Barberos",
                new[] { "Barbero", "Activo", "Citas", "Completadas", "Canceladas", "Ingresos", "Calificación (histórica)", "Reseñas" },
                r.Barberos.Select(b => new object?[] { b.Nombre, b.Activo ? "Sí" : "No", b.Citas, b.Completadas, b.Canceladas, b.Ingresos,
                                                       b.Calificacion.HasValue ? b.Calificacion.Value : null, b.Resenas }),
                monedaCols: new[] { 6 });

            HojaTabla(wb, "Servicios",
                new[] { "Servicio", "Veces vendido", "Monto (precio de lista)" },
                r.TopServicios.Select(s => new object?[] { s.Nombre, s.Veces, s.Monto }),
                monedaCols: new[] { 3 });

            HojaTabla(wb, "Ingresos por día",
                new[] { "Día", "Citas pagadas", "Ingresos" },
                r.IngresosPorDia.Select(d => new object?[] { d.Dia, d.Citas, d.Ingresos }),
                monedaCols: new[] { 3 }, fechaCols: new[] { 1 }, total: true);

            HojaTabla(wb, "Gastos",
                new[] { "Fecha", "Concepto", "Monto" },
                r.Gastos.Select(g => new object?[] { g.Fecha.Date, g.Concepto, g.Monto }),
                monedaCols: new[] { 3 }, fechaCols: new[] { 1 }, total: true);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static void HojaTabla(XLWorkbook wb, string nombre, string[] encabezados, IEnumerable<object?[]> filas,
                                      int[]? monedaCols = null, int[]? fechaCols = null, bool total = false)
        {
            var ws = wb.Worksheets.Add(nombre);
            for (var c = 0; c < encabezados.Length; c++) ws.Cell(1, c + 1).Value = encabezados[c];

            var fila = 2;
            foreach (var datos in filas)
            {
                for (var c = 0; c < datos.Length; c++)
                    ws.Cell(fila, c + 1).Value = datos[c] is null ? Blank.Value : XLCellValue.FromObject(datos[c]);
                fila++;
            }

            var ultima = Math.Max(fila - 1, 2);
            foreach (var c in monedaCols ?? Array.Empty<int>()) ws.Range(2, c, ultima, c).Style.NumberFormat.Format = FormatoMoneda;
            foreach (var c in fechaCols ?? Array.Empty<int>()) ws.Range(2, c, ultima, c).Style.DateFormat.Format = "dd/MM/yyyy";

            var tabla = EstiloTabla(ws.Range(1, 1, ultima, encabezados.Length), nombre.Replace(" ", ""));
            if (total && fila > 2)
            {
                tabla.ShowTotalsRow = true;
                tabla.Field(encabezados.Length - 1).TotalsRowFunction = XLTotalsRowFunction.Sum;
                tabla.Field(0).TotalsRowLabel = "Total";
            }
            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents(1, 60);
        }

        private static IXLTable EstiloTabla(IXLRange rango, string nombre)
        {
            var tabla = rango.CreateTable(nombre);
            tabla.Theme = XLTableTheme.TableStyleMedium4;
            tabla.ShowAutoFilter = true;
            return tabla;
        }

        // ============================== PDF ==============================

        public byte[] GenerarPdf(ReporteAdmin r)
        {
            var res = r.Resumen;
            var oro = "#9C7A1C";
            var gris = Colors.Grey.Darken1;

            return Document.Create(doc => doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(32);
                page.DefaultTextStyle(t => t.FontSize(9).FontColor(Colors.Grey.Darken4));

                page.Header().PaddingBottom(10).BorderBottom(1).BorderColor(oro).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("LUMINA RESERVE").FontSize(18).Bold().FontColor(oro).LetterSpacing(0.08f);
                        c.Item().Text("Reporte del panel de administración").FontSize(10).FontColor(gris);
                    });
                    row.ConstantItem(200).AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text($"Sucursal {r.Filtro.SucursalNombre}").Bold().FontSize(10);
                        c.Item().AlignRight().Text(r.Filtro.Etiqueta).FontColor(gris);
                        c.Item().AlignRight().Text($"Generado {r.Generado:dd/MM/yyyy HH:mm}").FontSize(8).FontColor(gris);
                    });
                });

                page.Content().PaddingTop(14).Column(col =>
                {
                    col.Spacing(16);

                    // KPIs en dos filas de tarjetas
                    col.Item().Column(k =>
                    {
                        k.Spacing(8);
                        k.Item().Row(row =>
                        {
                            row.Spacing(8);
                            Tarjeta(row, "Ingresos", Moneda(res.Ingresos), "Pagos completados");
                            Tarjeta(row, "Gastos", Moneda(res.Gastos), $"{r.Gastos.Count} registros");
                            Tarjeta(row, "Utilidad neta", Moneda(res.Utilidad), res.Utilidad >= 0 ? "Ganancia" : "Pérdida");
                        });
                        k.Item().Row(row =>
                        {
                            row.Spacing(8);
                            Tarjeta(row, "Citas", res.CitasTotal.ToString(), $"{res.Completadas} completadas · {res.Canceladas} canceladas");
                            Tarjeta(row, "Ticket promedio", Moneda(res.TicketPromedio), $"{res.ClientesUnicos} clientes atendidos");
                            Tarjeta(row, "Calificación", res.CalificacionPromedio.HasValue ? $"{res.CalificacionPromedio.Value:0.0} / 5" : "—", res.TotalResenas == 1 ? "1 reseña" : $"{res.TotalResenas} reseñas");
                        });
                    });

                    Seccion(col, "Rendimiento por barbero", r.Barberos.Count == 0 ? null : t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(1.4f); c.RelativeColumn(1.2f); });
                        Encabezados(t, "Barbero", ">Citas", ">Completadas", ">Canceladas", ">Ingresos", ">Calificación");
                        foreach (var b in r.Barberos)
                        {
                            Celda(t, b.Nombre + (b.Activo ? "" : " (inactivo)"));
                            Celda(t, b.Citas.ToString(), true);
                            Celda(t, b.Completadas.ToString(), true);
                            Celda(t, b.Canceladas.ToString(), true);
                            Celda(t, Moneda(b.Ingresos), true);
                            Celda(t, b.Calificacion.HasValue ? $"{b.Calificacion.Value:0.0} ({b.Resenas})" : "—", true);
                        }
                    });

                    Seccion(col, "Servicios más vendidos", r.TopServicios.Count == 0 ? null : t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(); c.RelativeColumn(1.4f); });
                        Encabezados(t, "Servicio", ">Veces", ">Monto");
                        foreach (var s in r.TopServicios)
                        {
                            Celda(t, s.Nombre); Celda(t, s.Veces.ToString(), true); Celda(t, Moneda(s.Monto), true);
                        }
                    });

                    var dias = r.IngresosPorDia.Where(d => d.Ingresos > 0).ToList();
                    Seccion(col, "Ingresos por día", dias.Count == 0 ? null : t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(); c.RelativeColumn(1.4f); });
                        Encabezados(t, "Día", ">Citas pagadas", ">Ingresos");
                        foreach (var d in dias)
                        {
                            Celda(t, d.Dia.ToString("dddd dd/MM/yyyy", Es)); Celda(t, d.Citas.ToString(), true); Celda(t, Moneda(d.Ingresos), true);
                        }
                        Total(t, 2, Moneda(dias.Sum(d => d.Ingresos)));
                    });

                    Seccion(col, "Gastos", r.Gastos.Count == 0 ? null : t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(1.2f); c.RelativeColumn(3); c.RelativeColumn(1.4f); });
                        Encabezados(t, "Fecha", "Concepto", ">Monto");
                        foreach (var g in r.Gastos)
                        {
                            Celda(t, g.Fecha.ToString("dd/MM/yyyy")); Celda(t, g.Concepto); Celda(t, Moneda(g.Monto), true);
                        }
                        Total(t, 2, Moneda(r.Gastos.Sum(g => g.Monto)));
                    });

                    Seccion(col, $"Citas del periodo ({r.Agenda.Count})", r.Agenda.Count == 0 ? null : t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(1.5f); c.RelativeColumn(2); c.RelativeColumn(1.8f); c.RelativeColumn(2.6f); c.RelativeColumn(1.1f); c.RelativeColumn(1.2f);
                        });
                        Encabezados(t, "Fecha", "Cliente", "Barbero", "Servicios", ">Total", "Estado");
                        foreach (var c in r.Agenda.OrderBy(x => x.FechaHora))
                        {
                            Celda(t, c.FechaHora.ToString("dd/MM/yy HH:mm"));
                            Celda(t, c.Cliente + (c.TieneCuenta ? "" : " (invitado)"));
                            Celda(t, c.Barbero);
                            Celda(t, string.IsNullOrEmpty(c.Servicios) ? "—" : c.Servicios);
                            Celda(t, Moneda(c.Total), true);
                            Celda(t, c.Estado);
                        }
                    });
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(gris));
                    t.Span("Lumina Reserve · Página ");
                    t.CurrentPageNumber();
                    t.Span(" de ");
                    t.TotalPages();
                });
            })).GeneratePdf();
        }

        private static string Moneda(decimal v) => v.ToString("C", Es);

        private static void Tarjeta(RowDescriptor row, string titulo, string valor, string detalle) =>
            row.RelativeItem().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(9).Column(c =>
            {
                c.Item().Text(titulo).FontSize(8).FontColor(Colors.Grey.Darken1);
                c.Item().PaddingTop(2).Text(valor).FontSize(14).Bold();
                c.Item().Text(detalle).FontSize(7.5f).FontColor(Colors.Grey.Darken1);
            });

        private static void Seccion(ColumnDescriptor col, string titulo, Action<TableDescriptor>? tabla)
        {
            col.Item().Column(c =>
            {
                c.Item().PaddingBottom(5).Text(titulo).FontSize(11).Bold();
                if (tabla == null) c.Item().Text("Sin datos en este periodo.").FontColor(Colors.Grey.Darken1).Italic();
                else c.Item().Table(tabla);
            });
        }

        // Un encabezado que empieza con ">" se alinea a la derecha (columnas numéricas).
        private static void Encabezados(TableDescriptor t, params string[] textos) =>
            t.Header(h =>
            {
                foreach (var texto in textos)
                {
                    var celda = h.Cell().Background("#F4EEDD").BorderBottom(1).BorderColor("#9C7A1C").PaddingVertical(4).PaddingHorizontal(5);
                    (texto.StartsWith('>') ? celda.AlignRight() : celda).Text(texto.TrimStart('>')).Bold().FontSize(8);
                }
            });

        private static void Celda(TableDescriptor t, string texto, bool derecha = false)
        {
            var celda = t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3.5f).PaddingHorizontal(5);
            (derecha ? celda.AlignRight() : celda).Text(texto);
        }

        private static void Total(TableDescriptor t, int columnasAntes, string valor)
        {
            t.Cell().ColumnSpan((uint)columnasAntes).PaddingVertical(4).PaddingHorizontal(5).AlignRight().Text("Total").Bold();
            t.Cell().PaddingVertical(4).PaddingHorizontal(5).AlignRight().Text(valor).Bold();
        }
    }
}
