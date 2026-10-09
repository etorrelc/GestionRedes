using System.Data;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace GestionRedes;

public partial class MainWindow : Window
{
    private DataTable tablaEntrada;
    private Transporte ultimoModelo;
    private double[,] ultimaAsignacionOptima;
    private string procedimientoCompleto = "";
    private int dimensionM = 3;
    private int dimensionN = 4;

    public MainWindow()
    {
        InitializeComponent();
        CargarEjemplo();
    }

    // ========================================================================
    // 1. CONFIGURACIÓN Y MANEJO DE LA MATRIZ DE ENTRADA (DATATABLE)
    // ========================================================================
    private void ConstruirTabla(int m, int n)
    {
        dimensionM = m;
        dimensionN = n;
        tablaEntrada = new DataTable();

        // Columna 0: Orígenes / Nombres
        tablaEntrada.Columns.Add("OrigenDestino", typeof(string));

        // Columnas 1..N: Destinos
        for (int j = 0; j < n; j++)
        {
            tablaEntrada.Columns.Add("D" + j, typeof(string));
        }

        // Última columna: Oferta
        tablaEntrada.Columns.Add("Oferta", typeof(string));

        // Fila 0: Nombres de los destinos
        var filaNombresDst = tablaEntrada.NewRow();
        filaNombresDst[0] = "Nombre Destino →";
        for (int j = 0; j < n; j++)
        {
            filaNombresDst[j + 1] = "Destino " + (j + 1);
        }
        filaNombresDst[n + 1] = "-";
        tablaEntrada.Rows.Add(filaNombresDst);

        // Filas 1..m: Data Centers
        for (int i = 0; i < m; i++)
        {
            var fila = tablaEntrada.NewRow();
            fila[0] = "Data Center " + (i + 1);
            for (int j = 0; j < n; j++)
            {
                fila[j + 1] = "10";
            }
            fila[n + 1] = "100";
            tablaEntrada.Rows.Add(fila);
        }

        // Fila m+1: Demanda
        var filaDemanda = tablaEntrada.NewRow();
        filaDemanda[0] = "Demanda (Mbps)";
        for (int j = 0; j < n; j++)
        {
            filaDemanda[j + 1] = "50";
        }
        filaDemanda[n + 1] = "-";
        tablaEntrada.Rows.Add(filaDemanda);

        gridEntrada.ItemsSource = tablaEntrada.DefaultView;
    }

    private void CargarEjemplo()
    {
        cbNumOrg.SelectedIndex = 2; // 3
        cbNumDst.SelectedIndex = 3; // 4
        ConstruirTabla(3, 4);

        string[] org = { "DC Lima (Central)", "DC Trujillo (Norte)", "DC Arequipa (Sur)" };
        string[] dst = { "POP Cajamarca", "POP Piura", "POP Cusco", "POP Iquitos" };
        double[,] c = { { 28, 32, 20, 45 }, { 12, 18, 35, 40 }, { 30, 38, 10, 55 } };
        double[] of = { 400, 300, 250 };
        double[] dm = { 200, 180, 220, 250 };

        // Fila 0: Destinos
        for (int j = 0; j < 4; j++)
        {
            tablaEntrada.Rows[0][j + 1] = dst[j];
            tablaEntrada.Rows[4][j + 1] = dm[j].ToString(CultureInfo.InvariantCulture);
        }

        // Filas 1..3: Orígenes
        for (int i = 0; i < 3; i++)
        {
            tablaEntrada.Rows[i + 1][0] = org[i];
            tablaEntrada.Rows[i + 1][5] = of[i].ToString(CultureInfo.InvariantCulture);
            for (int j = 0; j < 4; j++)
            {
                tablaEntrada.Rows[i + 1][j + 1] = c[i, j].ToString(CultureInfo.InvariantCulture);
            }
        }
    }

    private static bool ParseNum(object obj, out double val)
    {
        string s = Convert.ToString(obj)?.Trim().Replace(',', '.') ?? "";
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out val) && val >= 0;
    }

    private Transporte LeerDatosDesdeGrid()
    {
        int m = dimensionM;
        int n = dimensionN;

        var org = new List<string>();
        var dst = new List<string>();
        var of = new List<double>();
        var dm = new List<double>();
        var c = new double[m, n];

        // Leer destinos (Fila 0) y demandas (Fila m+1)
        for (int j = 0; j < n; j++)
        {
            string nombreDst = Convert.ToString(tablaEntrada.Rows[0][j + 1])?.Trim();
            if (string.IsNullOrEmpty(nombreDst)) nombreDst = "Destino " + (j + 1);
            dst.Add(nombreDst);

            if (!ParseNum(tablaEntrada.Rows[m + 1][j + 1], out double demanda))
            {
                MessageBox.Show($"La demanda del destino '{nombreDst}' no es válida.", "Error de Entrada", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            dm.Add(demanda);
        }

        // Leer orígenes (Filas 1..m), costos y oferta
        for (int i = 0; i < m; i++)
        {
            string nombreOrg = Convert.ToString(tablaEntrada.Rows[i + 1][0])?.Trim();
            if (string.IsNullOrEmpty(nombreOrg)) nombreOrg = "Data Center " + (i + 1);
            org.Add(nombreOrg);

            if (!ParseNum(tablaEntrada.Rows[i + 1][n + 1], out double oferta))
            {
                MessageBox.Show($"La oferta del origen '{nombreOrg}' no es válida.", "Error de Entrada", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            of.Add(oferta);

            for (int j = 0; j < n; j++)
            {
                if (!ParseNum(tablaEntrada.Rows[i + 1][j + 1], out c[i, j]))
                {
                    MessageBox.Show($"El costo de enlace ({nombreOrg} → {dst[j]}) no es válido.", "Error de Entrada", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }
            }
        }

        return new Transporte(org, dst, of, dm, c);
    }

    private void GridEntrada_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (e.PropertyName == "OrigenDestino")
        {
            e.Column.Header = "Origen \\ Destino";
            e.Column.Width = new DataGridLength(1.6, DataGridLengthUnitType.Star);
        }
        else if (e.PropertyName == "Oferta")
        {
            e.Column.Header = "Capacidad Oferta (Mbps)";
            e.Column.Width = new DataGridLength(1.5, DataGridLengthUnitType.Star);
        }
        else
        {
            int colIdx = 0;
            if (int.TryParse(e.PropertyName.Replace("D", ""), out int num)) colIdx = num + 1;
            e.Column.Header = $"Destino {colIdx}";
            e.Column.Width = new DataGridLength(1.2, DataGridLengthUnitType.Star);
        }
    }

    private void GridResultados_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        e.Column.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
    }

    // ========================================================================
    // 2. RESOLUCIÓN DE MODELOS Y ALGORITMOS
    // ========================================================================
    private void BtnResolver_Click(object sender, RoutedEventArgs e)
    {
        var modeloBase = LeerDatosDesdeGrid();
        if (modeloBase == null) return;

        modeloBase.Balancear();

        // 1. Ejecutar comparativa general de todos los métodos
        var resultadosComparativa = Transporte.CompararTodos(modeloBase);
        LlenarCuadroComparativo(resultadosComparativa);

        // 2. Resolver según el método seleccionado en el ComboBox
        int metodoSel = cbMetodo.SelectedIndex;
        string nombreMetodo;
        double[,] xIni;
        bool[,] bIni;
        var modeloActivo = modeloBase.Clonar();

        switch (metodoSel)
        {
            case 1:
                nombreMetodo = "Costo Mínimo";
                (xIni, bIni) = modeloActivo.CostoMinimo();
                break;
            case 2:
                nombreMetodo = "Esquina Noroeste";
                (xIni, bIni) = modeloActivo.EsquinaNoroeste();
                break;
            default:
                nombreMetodo = "Vogel (VAM)";
                (xIni, bIni) = modeloActivo.Vogel();
                break;
        }

        double costoIni = modeloActivo.CostoTotal(xIni);
        LlenarGrillaSolucion(gridInicial, modeloActivo, xIni);

        // Optimización MODI
        int iteraciones = modeloActivo.OptimizarMODI(xIni, bIni);
        procedimientoCompleto = modeloActivo.Log.ToString();

        if (iteraciones < 0)
        {
            MessageBox.Show("Aviso: No se pudo completar la optimización en este caso.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        double costoOpt = modeloActivo.CostoTotal(xIni);
        LlenarGrillaSolucion(gridOptima, modeloActivo, xIni);

        ultimoModelo = modeloActivo;
        ultimaAsignacionOptima = xIni;

        // Actualizar tarjetas de KPI
        lblKpiInicial.Text = $"{costoIni:N0}";
        lblKpiOptimo.Text = $"{costoOpt:N0}";
        double ahorro = costoIni - costoOpt;
        double pctAhorro = costoIni > 0 ? (ahorro / costoIni) * 100 : 0;
        lblKpiAhorro.Text = $"{ahorro:N0} ({pctAhorro:0.#}%)";
        lblKpiBalanceo.Text = string.IsNullOrEmpty(modeloBase.NotaBalanceo) ? "Balanceada" : "Ajustada (Ficticio)";

        // Generar topología vectorial en el Canvas
        DibujarTopologiaVectorial();

        // Generar reporte de enrutamiento textual
        GenerarReporteTexto(nombreMetodo, costoIni, costoOpt, ahorro, pctAhorro, iteraciones);

        // Seleccionar pestaña correspondiente
        if (metodoSel == 3)
        {
            tabsPrincipal.SelectedIndex = 1; // Cuadro Comparativo
        }
        else
        {
            tabsPrincipal.SelectedIndex = 0; // Topología de Red
        }
    }

    private void LlenarCuadroComparativo(List<MetodoResultado> lista)
    {
        var vmList = new List<ComparativaItemViewModel>();
        foreach (var r in lista)
        {
            string estado = "Subóptima";
            if (r.Tipo.Contains("Exacta")) estado = "★ Solución Global";
            else if (r.EsMejorInicial) estado = "✓ Mejor Inicial";

            vmList.Add(new ComparativaItemViewModel
            {
                Metodo = r.Metodo,
                Tipo = r.Tipo,
                CostoFormateado = r.CostoTotal.ToString("N0", CultureInfo.CurrentCulture),
                DiferenciaFormateada = r.DiferenciaVsOptimo > 0 ? "+" + r.DiferenciaVsOptimo.ToString("N0") : "0 (Óptimo)",
                PorcentajeFormateado = r.PorcentajeDesviacion > 0 ? "+" + r.PorcentajeDesviacion.ToString("0.##") + "%" : "0.00%",
                IteracionesTexto = r.Tipo.Contains("Inicial") ? (r.IteracionesMODI >= 0 ? r.IteracionesMODI.ToString() : "N/A") : "-",
                EstadoTexto = estado
            });
        }
        gridComparativa.ItemsSource = vmList;
    }

    private void LlenarGrillaSolucion(DataGrid grid, Transporte p, double[,] x)
    {
        var dt = new DataTable();
        dt.Columns.Add("Origen", typeof(string));
        foreach (var d in p.Dst) dt.Columns.Add(d, typeof(string));
        dt.Columns.Add("Total Despachado", typeof(string));

        for (int i = 0; i < p.M; i++)
        {
            var row = dt.NewRow();
            row[0] = p.Org[i];
            double tot = 0;
            for (int j = 0; j < p.N; j++)
            {
                if (x[i, j] > Transporte.EPS)
                {
                    row[j + 1] = $"{x[i, j]:0.##} Mbps";
                    tot += x[i, j];
                }
                else
                {
                    row[j + 1] = "-";
                }
            }
            row[p.N + 1] = $"{tot:0.##} Mbps";
            dt.Rows.Add(row);
        }

        grid.ItemsSource = dt.DefaultView;
    }

    private void GenerarReporteTexto(string metodoSel, double costoIni, double costoOpt, double ahorro, double pct, int iter)
    {
        var sb = new StringBuilder();
        sb.AppendLine("===============================================================================");
        sb.AppendLine("           PLAN DE ASIGNACIÓN Y ENRUTAMIENTO ÓPTIMO DE TRÁFICO");
        sb.AppendLine("===============================================================================");
        if (!string.IsNullOrEmpty(ultimoModelo.NotaBalanceo))
        {
            sb.AppendLine("[AVISO DE BALANCEO]: " + ultimoModelo.NotaBalanceo);
            sb.AppendLine();
        }

        sb.AppendLine(string.Format("{0,-30} → {1,-26} {2,12} {3,16}", "ORIGEN (DATA CENTER)", "DESTINO (CLIENTE)", "FLUJO (Mbps)", "COSTO/LATENCIA"));
        sb.AppendLine(new string('-', 88));

        for (int i = 0; i < ultimoModelo.M; i++)
        {
            for (int j = 0; j < ultimoModelo.N; j++)
            {
                if (ultimaAsignacionOptima[i, j] > Transporte.EPS)
                {
                    double costoEnlace = ultimaAsignacionOptima[i, j] * ultimoModelo.C[i, j];
                    sb.AppendLine(string.Format("{0,-30} → {1,-26} {2,10:0.##} Mbps {3,16}",
                        ultimoModelo.Org[i],
                        ultimoModelo.Dst[j],
                        ultimaAsignacionOptima[i, j],
                        $"{ultimoModelo.C[i, j]:0.##} (Total: {costoEnlace:N0})"));
                }
            }
        }
        sb.AppendLine(new string('-', 88));
        sb.AppendLine($"Costo Total Mínimo Global: {costoOpt:N0}");
        sb.AppendLine($"Método Inicial Seleccionado: {metodoSel} (Costo Inicial: {costoIni:N0})");
        sb.AppendLine($"Ahorro logrado por MODI: {ahorro:N0} ({pct:0.##}%) en {iter} iteración(es)");
        sb.AppendLine();
        sb.AppendLine("-------------------------------------------------------------------------------");
        sb.AppendLine("DETALLE MATEMÁTICO PASO A PASO (LOG DE INVESTIGACIÓN DE OPERACIONES):");
        sb.AppendLine("-------------------------------------------------------------------------------");
        sb.AppendLine(procedimientoCompleto);

        txtReporte.Text = sb.ToString();
    }

    // ========================================================================
    // 3. TOPOLOGÍA DE RED VECTORIAL EN CANVAS (WPF)
    // ========================================================================
    private void DibujarTopologiaVectorial()
    {
        if (ultimoModelo == null || ultimaAsignacionOptima == null) return;
        canvasTopologia.Children.Clear();

        double width = Math.Max(canvasTopologia.ActualWidth, 850);
        double height = Math.Max(canvasTopologia.ActualHeight, 460);

        int m = ultimoModelo.M;
        int n = ultimoModelo.N;

        double nodeWidth = 200;
        double nodeHeight = 44;
        double startY = 40;

        double origX = 30;
        double destX = Math.Max(width - nodeWidth - 40, origX + nodeWidth + 260);

        double spaceOrig = (height - startY - 30) / (m + 1);
        double spaceDest = (height - startY - 30) / (n + 1);

        var origPoints = new Point[m];
        var destPoints = new Point[n];

        // 1. Calcular puntos de conexión de orígenes y dibujar nodos
        for (int i = 0; i < m; i++)
        {
            double y = startY + (i + 1) * spaceOrig - nodeHeight / 2;
            origPoints[i] = new Point(origX + nodeWidth, y + nodeHeight / 2);

            double despachado = 0;
            for (int j = 0; j < n; j++) despachado += ultimaAsignacionOptima[i, j];

            var nodoBorder = CrearNodoWpf(
                ultimoModelo.Org[i],
                $"Capacidad: {ultimoModelo.S[i]:0.##} Mbps",
                $"Despachado: {despachado:0.##} Mbps",
                Color.FromRgb(30, 41, 59),
                Color.FromRgb(37, 99, 235),
                nodeWidth, nodeHeight);

            Canvas.SetLeft(nodoBorder, origX);
            Canvas.SetTop(nodoBorder, y);
            canvasTopologia.Children.Add(nodoBorder);
        }

        // 2. Calcular puntos de conexión de destinos y dibujar nodos
        for (int j = 0; j < n; j++)
        {
            double y = startY + (j + 1) * spaceDest - nodeHeight / 2;
            destPoints[j] = new Point(destX, y + nodeHeight / 2);

            double recibido = 0;
            for (int i = 0; i < m; i++) recibido += ultimaAsignacionOptima[i, j];

            var nodoBorder = CrearNodoWpf(
                ultimoModelo.Dst[j],
                $"Demanda: {ultimoModelo.D[j]:0.##} Mbps",
                $"Recibido: {recibido:0.##} Mbps",
                Color.FromRgb(15, 118, 110),
                Color.FromRgb(16, 185, 129),
                nodeWidth, nodeHeight);

            Canvas.SetLeft(nodoBorder, destX);
            Canvas.SetTop(nodoBorder, y);
            canvasTopologia.Children.Add(nodoBorder);
        }

        // 3. Dibujar curvas Bezier de enlaces
        double maxFlujo = 1.0;
        double minCosto = double.MaxValue, maxCosto = double.MinValue;
        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (ultimaAsignacionOptima[i, j] > maxFlujo) maxFlujo = ultimaAsignacionOptima[i, j];
                if (ultimoModelo.C[i, j] < minCosto) minCosto = ultimoModelo.C[i, j];
                if (ultimoModelo.C[i, j] > maxCosto) maxCosto = ultimoModelo.C[i, j];
            }
        }

        bool soloActivos = chkSoloActivos.IsChecked == true;

        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < n; j++)
            {
                double flujo = ultimaAsignacionOptima[i, j];
                bool esActivo = flujo > Transporte.EPS;

                if (!esActivo && soloActivos) continue;

                Point ptIni = origPoints[i];
                Point ptFin = destPoints[j];
                double dx = ptFin.X - ptIni.X;
                Point cp1 = new Point(ptIni.X + dx * 0.45, ptIni.Y);
                Point cp2 = new Point(ptFin.X - dx * 0.45, ptFin.Y);

                // Crear geometría de la curva
                var fig = new PathFigure { StartPoint = ptIni, IsClosed = false };
                fig.Segments.Add(new BezierSegment(cp1, cp2, ptFin, true));
                var geom = new PathGeometry();
                geom.Figures.Add(fig);

                var path = new Path { Data = geom };

                if (!esActivo)
                {
                    path.Stroke = new SolidColorBrush(Color.FromRgb(226, 232, 240));
                    path.StrokeThickness = 1.2;
                    path.StrokeDashArray = new DoubleCollection { 3, 3 };
                }
                else
                {
                    double grosor = Math.Clamp(2.0 + (flujo / maxFlujo) * 6.5, 2.0, 9.0);
                    path.StrokeThickness = grosor;

                    bool esFicticio = ultimoModelo.Org[i].Contains("ficticio") || ultimoModelo.Org[i].Contains("Demanda no atendida") ||
                                      ultimoModelo.Dst[j].Contains("ficticio") || ultimoModelo.Dst[j].Contains("Capacidad ociosa");

                    Color colorLinea;
                    if (esFicticio)
                    {
                        colorLinea = Color.FromRgb(148, 163, 184);
                        path.StrokeDashArray = new DoubleCollection { 4, 3 };
                    }
                    else
                    {
                        double rango = Math.Max(maxCosto - minCosto, 1.0);
                        double factor = (ultimoModelo.C[i, j] - minCosto) / rango;
                        if (factor < 0.4)
                            colorLinea = Color.FromRgb(16, 185, 129); // Verde
                        else if (factor < 0.7)
                            colorLinea = Color.FromRgb(37, 99, 235);  // Azul
                        else
                            colorLinea = Color.FromRgb(239, 68, 68);  // Rojo
                    }
                    path.Stroke = new SolidColorBrush(colorLinea);

                    // Badge de tráfico en el punto medio
                    double midX = 0.125 * ptIni.X + 0.375 * cp1.X + 0.375 * cp2.X + 0.125 * ptFin.X;
                    double midY = 0.125 * ptIni.Y + 0.375 * cp1.Y + 0.375 * cp2.Y + 0.125 * ptFin.Y;

                    var badge = CrearBadgeWpf($"{flujo:0.##} Mbps (c={ultimoModelo.C[i, j]:0.##})", colorLinea);
                    Canvas.SetLeft(badge, midX - 65);
                    Canvas.SetTop(badge, midY - 11);
                    canvasTopologia.Children.Add(badge);
                }

                canvasTopologia.Children.Add(path);
            }
        }
    }

    private static Border CrearNodoWpf(string titulo, string sub1, string sub2, Color colBarra, Color colAcento, double w, double h)
    {
        var border = new Border
        {
            Width = w,
            Height = h,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            ToolTip = $"{titulo}\n{sub1}\n{sub2}"
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var barra = new Rectangle { Fill = new SolidColorBrush(colBarra) };
        Grid.SetColumn(barra, 0);
        grid.Children.Add(barra);

        var stack = new StackPanel { Margin = new Thickness(8, 4, 6, 4) };
        var txtT = new TextBlock { Text = titulo, FontWeight = FontWeights.Bold, FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)) };
        var subGrid = new Grid();
        subGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        subGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var txtSub1 = new TextBlock { Text = sub1, FontSize = 9.5, Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)) };
        var txtSub2 = new TextBlock { Text = sub2, FontSize = 9.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(colAcento), HorizontalAlignment = HorizontalAlignment.Right };
        Grid.SetColumn(txtSub1, 0);
        Grid.SetColumn(txtSub2, 1);
        subGrid.Children.Add(txtSub1);
        subGrid.Children.Add(txtSub2);

        stack.Children.Add(txtT);
        stack.Children.Add(subGrid);
        Grid.SetColumn(stack, 1);
        grid.Children.Add(stack);

        border.Child = grid;
        return border;
    }

    private static Border CrearBadgeWpf(string texto, Color colBorde)
    {
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(colBorde),
            BorderThickness = new Thickness(1.2),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 2),
            Child = new TextBlock
            {
                Text = texto,
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
            }
        };
    }

    // ========================================================================
    // 4. EVENTOS Y ACCIONES DE LA INTERFAZ
    // ========================================================================
    private void BtnCrearTabla_Click(object sender, RoutedEventArgs e)
    {
        int m = cbNumOrg.SelectedIndex + 1;
        int n = cbNumDst.SelectedIndex + 1;
        ConstruirTabla(m, n);
    }

    private void BtnCargarEjemplo_Click(object sender, RoutedEventArgs e)
    {
        CargarEjemplo();
    }

    private void ChkSoloActivos_Click(object sender, RoutedEventArgs e)
    {
        DibujarTopologiaVectorial();
    }

    private void CanvasTopologia_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DibujarTopologiaVectorial();
    }

    private void BtnCopiarDiagrama_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            double w = canvasTopologia.ActualWidth;
            double h = canvasTopologia.ActualHeight;
            if (w <= 0 || h <= 0) return;

            var rtb = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(canvasTopologia);
            Clipboard.SetImage(rtb);
            MessageBox.Show("¡Diagrama copiado al portapapeles exitosamente!", "Copiado", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error al copiar imagen: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void BtnProcedimiento_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(procedimientoCompleto))
        {
            BtnResolver_Click(sender, e);
        }
        if (string.IsNullOrEmpty(procedimientoCompleto)) return;

        var win = new Window
        {
            Title = "Procedimiento Paso a Paso (Investigación de Operaciones)",
            Width = 920,
            Height = 680,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42))
        };
        var tb = new TextBox
        {
            Text = procedimientoCompleto,
            IsReadOnly = true,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
            Foreground = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(14)
        };
        win.Content = tb;
        win.ShowDialog();
    }
}

public class ComparativaItemViewModel
{
    public string Metodo { get; set; }
    public string Tipo { get; set; }
    public string CostoFormateado { get; set; }
    public string DiferenciaFormateada { get; set; }
    public string PorcentajeFormateado { get; set; }
    public string IteracionesTexto { get; set; }
    public string EstadoTexto { get; set; }
}
