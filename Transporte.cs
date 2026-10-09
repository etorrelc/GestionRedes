using System.Globalization;
using System.Text;

public class MetodoResultado
{
    public string Metodo { get; set; } = "";
    public string Tipo { get; set; } = "";
    public double CostoTotal { get; set; }
    public double DiferenciaVsOptimo { get; set; }
    public double PorcentajeDesviacion { get; set; }
    public int IteracionesMODI { get; set; }
    public bool EsMejorInicial { get; set; }
    public double[,] Asignacion { get; set; } = new double[0, 0];
    public bool[,] Base { get; set; } = new bool[0, 0];
    public string Procedimiento { get; set; } = "";
}

public class Transporte
{
    public List<string> Org, Dst;
    public List<double> S, D;
    public double[,] C;
    public int M => S.Count;
    public int N => D.Count;
    public string NotaBalanceo = "";
    public StringBuilder Log = new();
    public const double EPS = 1e-9;

    public static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    public Transporte(List<string> o, List<string> d, List<double> s, List<double> dem, double[,] c)
    {
        Org = new List<string>(o);
        Dst = new List<string>(d);
        S = new List<double>(s);
        D = new List<double>(dem);
        C = (double[,])c.Clone();
    }

    public Transporte Clonar()
    {
        var t = new Transporte(Org, Dst, S, D, C)
        {
            NotaBalanceo = NotaBalanceo
        };
        return t;
    }

    void LogCosto(double[,] x, string titulo)
    {
        var terms = new List<string>();
        double z = 0;
        for (int i = 0; i < M; i++)
        {
            for (int j = 0; j < N; j++)
            {
                if (x[i, j] > EPS)
                {
                    terms.Add($"{F(C[i, j])}×{F(x[i, j])}");
                    z += C[i, j] * x[i, j];
                }
            }
        }
        Log.AppendLine($"{titulo}: Z = {(terms.Count > 0 ? string.Join(" + ", terms) : "0")} = {F(z)}");
    }

    // ---------- Balanceo ----------
    public void Balancear()
    {
        double so = S.Sum();
        double dd = D.Sum();
        Log.AppendLine("=== BALANCEO DE LA RED ===");
        Log.AppendLine($"Oferta total = {F(so)} Mbps | Demanda total = {F(dd)} Mbps");
        if (Math.Abs(so - dd) < EPS)
        {
            Log.AppendLine("El problema ya está balanceado (oferta = demanda).");
            return;
        }

        if (so > dd)
        {
            var nc = new double[M, N + 1];
            for (int i = 0; i < M; i++)
                for (int j = 0; j < N; j++)
                    nc[i, j] = C[i, j];

            Dst.Add("(Capacidad ociosa)");
            D.Add(so - dd);
            C = nc;
            NotaBalanceo = $"Sobran {F(so - dd)} Mbps de capacidad: se agregó un nodo de destino ficticio.";
        }
        else
        {
            var nc = new double[M + 1, N];
            for (int i = 0; i < M; i++)
                for (int j = 0; j < N; j++)
                    nc[i, j] = C[i, j];

            Org.Add("(Demanda no atendida)");
            S.Add(dd - so);
            C = nc;
            NotaBalanceo = $"Faltan {F(dd - so)} Mbps de capacidad: se agregó un nodo de origen ficticio.";
        }
        Log.AppendLine(NotaBalanceo + " (costos de enlaces ficticios = 0)");
    }

    // ---------- DSU para manejo de degeneración ----------
    private class DisjointSet
    {
        private readonly int[] parent;
        public DisjointSet(int size)
        {
            parent = new int[size];
            for (int i = 0; i < size; i++) parent[i] = i;
        }
        public int Find(int i)
        {
            if (parent[i] == i) return i;
            return parent[i] = Find(parent[i]);
        }
        public bool Union(int i, int j)
        {
            int rootI = Find(i);
            int rootJ = Find(j);
            if (rootI != rootJ)
            {
                parent[rootI] = rootJ;
                return true;
            }
            return false;
        }
    }

    // Asegura que la base contenga exactamente m + n - 1 celdas y forme un árbol generador acíclico
    public void CompletarBaseDegenerada(bool[,] b, double[,] x)
    {
        int requeridas = M + N - 1;
        var dsu = new DisjointSet(M + N);
        int basicas = 0;

        for (int i = 0; i < M; i++)
        {
            for (int j = 0; j < N; j++)
            {
                if (b[i, j])
                {
                    dsu.Union(i, M + j);
                    basicas++;
                }
            }
        }

        if (basicas >= requeridas) return;

        Log.AppendLine();
        Log.AppendLine($"[TRATAMIENTO DE DEGENERACIÓN]: Se encontraron {basicas} celdas básicas (se requieren m + n - 1 = {requeridas}).");
        Log.AppendLine($"Se incorporan {requeridas - basicas} variable(s) básica(s) artificial(es) con flujo ε = 0 para garantizar conectividad en MODI.");

        // Candidatos ordenados por menor costo para mantener lógica económica
        var candidatos = new List<(int i, int j, double costo)>();
        for (int i = 0; i < M; i++)
        {
            for (int j = 0; j < N; j++)
            {
                if (!b[i, j]) candidatos.Add((i, j, C[i, j]));
            }
        }
        candidatos.Sort((a, bCell) => a.costo.CompareTo(bCell.costo));

        foreach (var c in candidatos)
        {
            if (basicas >= requeridas) break;
            if (dsu.Union(c.i, M + c.j))
            {
                b[c.i, c.j] = true;
                x[c.i, c.j] = 0;
                basicas++;
                Log.AppendLine($"   + Variable básica artificial ε asignada en celda ({Org[c.i]} → {Dst[c.j]}) con costo {F(C[c.i, c.j])}.");
            }
        }
    }

    // ---------- Método 1: Esquina Noroeste (ENO) ----------
    public (double[,], bool[,]) EsquinaNoroeste()
    {
        Log.AppendLine();
        Log.AppendLine("=== MÉTODO DE LA ESQUINA NOROESTE (ENO) ===");
        Log.AppendLine("Asignación secuencial iniciando en la celda superior izquierda (0,0).");

        var x = new double[M, N];
        var b = new bool[M, N];
        var s = S.ToArray();
        var d = D.ToArray();
        int i = 0, j = 0, paso = 1;

        while (i < M && j < N)
        {
            double so = s[i], dd = d[j];
            double q = Math.Min(so, dd);
            Log.AppendLine();
            Log.AppendLine($"Paso {paso++}: celda ({Org[i]} → {Dst[j]})");
            Log.AppendLine($"   Asignación: min(oferta {F(so)}, demanda {F(dd)}) = {F(q)}");
            x[i, j] = q;
            b[i, j] = true;
            s[i] -= q;
            d[j] -= q;
            Log.AppendLine($"   Oferta restante de {Org[i]}: {F(s[i])} | Demanda restante de {Dst[j]}: {F(d[j])}");

            if (s[i] < EPS && d[j] < EPS)
            {
                if (i < M - 1)
                {
                    Log.AppendLine($"   Se agotan simultáneamente: se avanza a la fila {Org[i + 1]}.");
                    i++;
                }
                else
                {
                    j++;
                }
            }
            else if (s[i] < EPS)
            {
                Log.AppendLine($"   Se agotó la oferta de {Org[i]}: avanzar a la siguiente fila.");
                i++;
            }
            else
            {
                Log.AppendLine($"   Se cubrió la demanda de {Dst[j]}: avanzar a la siguiente columna.");
                j++;
            }
        }

        CompletarBaseDegenerada(b, x);
        Log.AppendLine();
        LogCosto(x, "Costo de la solución inicial (ENO)");
        return (x, b);
    }

    // ---------- Método 2: Costo Mínimo ----------
    public (double[,], bool[,]) CostoMinimo()
    {
        Log.AppendLine();
        Log.AppendLine("=== MÉTODO DEL COSTO MÍNIMO (MATRIZ MÍNIMA) ===");
        Log.AppendLine("Se prioriza la celda activa con el menor costo/latencia unitario de toda la matriz.");

        var x = new double[M, N];
        var b = new bool[M, N];
        var s = S.ToArray();
        var d = D.ToArray();
        var fila = Enumerable.Repeat(true, M).ToArray();
        var col = Enumerable.Repeat(true, N).ToArray();
        int fAct = M, cAct = N, paso = 1;

        while (fAct > 0 && cAct > 0)
        {
            // Encontrar la celda activa de menor costo
            int rMin = -1, cMin = -1;
            double minCosto = double.MaxValue;

            for (int i = 0; i < M; i++)
            {
                if (!fila[i]) continue;
                for (int j = 0; j < N; j++)
                {
                    if (!col[j]) continue;
                    if (C[i, j] < minCosto)
                    {
                        minCosto = C[i, j];
                        rMin = i;
                        cMin = j;
                    }
                }
            }

            if (rMin == -1 || cMin == -1) break;

            double so = s[rMin], dd = d[cMin];
            double q = Math.Min(so, dd);

            Log.AppendLine();
            Log.AppendLine($"Paso {paso++}: Celda de menor costo global ({Org[rMin]} → {Dst[cMin]}) con c_{rMin + 1}{cMin + 1} = {F(minCosto)}");
            Log.AppendLine($"   Asignación: min(oferta {F(so)}, demanda {F(dd)}) = {F(q)}");

            x[rMin, cMin] = q;
            b[rMin, cMin] = true;
            s[rMin] -= q;
            d[cMin] -= q;

            Log.AppendLine($"   Oferta restante de {Org[rMin]}: {F(s[rMin])} | Demanda restante de {Dst[cMin]}: {F(d[cMin])}");

            if (s[rMin] < EPS && d[cMin] < EPS)
            {
                if (fAct > 1)
                {
                    fila[rMin] = false;
                    fAct--;
                    Log.AppendLine($"   Se agotan ambas capacidades: se tacha fila {Org[rMin]} (columna {Dst[cMin]} queda con saldo 0).");
                }
                else
                {
                    col[cMin] = false;
                    cAct--;
                    Log.AppendLine($"   Se agotan ambas capacidades: se tacha columna {Dst[cMin]}.");
                }
            }
            else if (s[rMin] < EPS)
            {
                fila[rMin] = false;
                fAct--;
                Log.AppendLine($"   Se agotó la oferta: se tacha la fila {Org[rMin]}.");
            }
            else
            {
                col[cMin] = false;
                cAct--;
                Log.AppendLine($"   Se satisfizo la demanda: se tacha la columna {Dst[cMin]}.");
            }
        }

        CompletarBaseDegenerada(b, x);
        Log.AppendLine();
        LogCosto(x, "Costo de la solución inicial (Costo Mínimo)");
        return (x, b);
    }

    // ---------- Método 3: Aproximación de Vogel (VAM) ----------
    public (double[,], bool[,]) Vogel()
    {
        Log.AppendLine();
        Log.AppendLine("=== MÉTODO DE APROXIMACIÓN DE VOGEL (VAM) ===");
        Log.AppendLine("Penalización = (segundo costo menor) - (costo menor) de cada fila/columna activa.");

        var x = new double[M, N];
        var b = new bool[M, N];
        var s = S.ToArray();
        var d = D.ToArray();
        var fila = Enumerable.Repeat(true, M).ToArray();
        var col = Enumerable.Repeat(true, N).ToArray();
        int fAct = M, cAct = N, it = 1;

        while (fAct > 0 && cAct > 0)
        {
            Log.AppendLine();
            Log.AppendLine($"--- Iteración {it++} ---");
            double mejorPen = -1;
            bool esFila = true;
            int idx = -1;

            for (int i = 0; i < M; i++)
            {
                if (!fila[i]) continue;
                var v = Enumerable.Range(0, N).Where(j => col[j]).Select(j => C[i, j]).OrderBy(z => z).ToList();
                double pen = v.Count > 1 ? v[1] - v[0] : v[0];
                Log.AppendLine(v.Count > 1
                    ? $"   Fila {Org[i]}: {F(v[1])} - {F(v[0])} = {F(pen)}"
                    : $"   Fila {Org[i]}: costo único {F(v[0])} → penalización {F(pen)}");
                if (pen > mejorPen) { mejorPen = pen; esFila = true; idx = i; }
            }
            for (int j = 0; j < N; j++)
            {
                if (!col[j]) continue;
                var v = Enumerable.Range(0, M).Where(i => fila[i]).Select(i => C[i, j]).OrderBy(z => z).ToList();
                double pen = v.Count > 1 ? v[1] - v[0] : v[0];
                Log.AppendLine(v.Count > 1
                    ? $"   Columna {Dst[j]}: {F(v[1])} - {F(v[0])} = {F(pen)}"
                    : $"   Columna {Dst[j]}: costo único {F(v[0])} → penalización {F(pen)}");
                if (pen > mejorPen) { mejorPen = pen; esFila = false; idx = j; }
            }

            int r, c;
            if (esFila)
            {
                r = idx; c = -1; double min = double.MaxValue;
                for (int j = 0; j < N; j++) if (col[j] && C[r, j] < min) { min = C[r, j]; c = j; }
            }
            else
            {
                c = idx; r = -1; double min = double.MaxValue;
                for (int i = 0; i < M; i++) if (fila[i] && C[i, c] < min) { min = C[i, c]; r = i; }
            }

            Log.AppendLine($"   >> Mayor penalización = {F(mejorPen)} en {(esFila ? "la fila " + Org[idx] : "la columna " + Dst[idx])}");
            Log.AppendLine($"   >> Celda de menor costo en esa línea: ({Org[r]} → {Dst[c]}) con costo {F(C[r, c])}");

            double so = s[r], dd = d[c];
            double q = Math.Min(so, dd);
            Log.AppendLine($"   Asignación: min(oferta {F(so)}, demanda {F(dd)}) = {F(q)}");
            x[r, c] = q; b[r, c] = true;
            s[r] -= q; d[c] -= q;
            Log.AppendLine($"   Oferta restante de {Org[r]}: {F(s[r])} | Demanda restante de {Dst[c]}: {F(d[c])}");

            if (s[r] < EPS && d[c] < EPS)
            {
                if (fAct > 1) { fila[r] = false; fAct--; Log.AppendLine($"   Se agotan ambas: se tacha la fila {Org[r]}."); }
                else { col[c] = false; cAct--; Log.AppendLine($"   Se agotan ambas: se tacha la columna {Dst[c]}."); }
            }
            else if (s[r] < EPS) { fila[r] = false; fAct--; Log.AppendLine($"   Se agotó la oferta: se tacha la fila {Org[r]}."); }
            else { col[c] = false; cAct--; Log.AppendLine($"   Se cubrió la demanda: se tacha la columna {Dst[c]}."); }
        }

        CompletarBaseDegenerada(b, x);
        Log.AppendLine();
        LogCosto(x, "Costo de la solución inicial (Vogel)");
        return (x, b);
    }

    // ---------- Optimización MODI (Distribución Modificada) ----------
    public int OptimizarMODI(double[,] x, bool[,] b)
    {
        Log.AppendLine();
        Log.AppendLine("=== OPTIMIZACIÓN EXACTA: MÉTODO MODI (u-v) ===");
        Log.AppendLine("Celdas básicas: u_i + v_j = c_ij (con u_1 = 0).");
        Log.AppendLine("Celdas no básicas: Δ_ij = c_ij - u_i - v_j. Si existe Δ < 0, la solución puede mejorarse.");

        int iter = 0;
        while (iter < 1000)
        {
            CompletarBaseDegenerada(b, x);

            Log.AppendLine();
            Log.AppendLine($"--- Iteración MODI {iter + 1} ---");

            var u = new double?[M];
            var v = new double?[N];
            u[0] = 0;
            Log.AppendLine($"   u[{Org[0]}] = 0 (valor de anclaje inicial)");

            bool cambio = true;
            while (cambio)
            {
                cambio = false;
                for (int i = 0; i < M; i++)
                {
                    for (int j = 0; j < N; j++)
                    {
                        if (!b[i, j]) continue;
                        if (u[i].HasValue && !v[j].HasValue)
                        {
                            v[j] = C[i, j] - u[i].Value;
                            Log.AppendLine($"   v[{Dst[j]}] = c({Org[i]},{Dst[j]}) - u[{Org[i]}] = {F(C[i, j])} - {F(u[i].Value)} = {F(v[j].Value)}");
                            cambio = true;
                        }
                        else if (!u[i].HasValue && v[j].HasValue)
                        {
                            u[i] = C[i, j] - v[j].Value;
                            Log.AppendLine($"   u[{Org[i]}] = c({Org[i]},{Dst[j]}) - v[{Dst[j]}] = {F(C[i, j])} - {F(v[j].Value)} = {F(u[i].Value)}");
                            cambio = true;
                        }
                    }
                }
            }

            Log.AppendLine("   Evaluación de costos marginales (Δ_ij = c_ij - u_i - v_j):");
            double minDelta = -EPS;
            int ei = -1, ej = -1;
            for (int i = 0; i < M; i++)
            {
                for (int j = 0; j < N; j++)
                {
                    if (b[i, j] || !u[i].HasValue || !v[j].HasValue) continue;
                    double delta = C[i, j] - u[i].Value - v[j].Value;
                    Log.AppendLine($"     Δ({Org[i]} → {Dst[j]}) = {F(C[i, j])} - ({F(u[i].Value)}) - ({F(v[j].Value)}) = {F(delta)}");
                    if (delta < minDelta)
                    {
                        minDelta = delta;
                        ei = i;
                        ej = j;
                    }
                }
            }

            if (ei == -1)
            {
                Log.AppendLine("   Todos los Δ_ij ≥ 0  →  ¡SE HA ALCANZADO LA SOLUCIÓN ÓPTIMA!");
                Log.AppendLine();
                LogCosto(x, "Costo óptimo global");
                return iter;
            }

            Log.AppendLine($"   >> Variable entrante más favorable: ({Org[ei]} → {Dst[ej]}) con Δ = {F(minDelta)}");

            var camino = new List<(int r, int c)> { (ei, ej) };
            if (!BuscarCiclo(camino, true, b))
            {
                // Intento alternativo en vertical
                camino = new List<(int r, int c)> { (ei, ej) };
                if (!BuscarCiclo(camino, false, b))
                {
                    Log.AppendLine("   Aviso: No se pudo cerrar el ciclo en la estructura actual.");
                    return -1;
                }
            }

            Log.AppendLine("   Circuito cerrado (Stepping-Stone con signos + y - alternados):");
            for (int k = 0; k < camino.Count; k++)
            {
                string signo = k % 2 == 0 ? "(+)" : "(-)";
                Log.AppendLine($"     {signo} ({Org[camino[k].r]} → {Dst[camino[k].c]})  valor actual = {F(x[camino[k].r, camino[k].c])}");
            }

            double theta = double.MaxValue;
            for (int k = 1; k < camino.Count; k += 2)
            {
                theta = Math.Min(theta, x[camino[k].r, camino[k].c]);
            }
            Log.AppendLine($"   Transferencia de flujo θ = min(celdas con '-') = {F(theta)} Mbps");

            int salida = -1;
            for (int k = 0; k < camino.Count; k++)
            {
                var (r, c) = camino[k];
                if (k % 2 == 0)
                {
                    x[r, c] += theta;
                }
                else
                {
                    x[r, c] -= theta;
                    if (salida == -1 && x[r, c] < EPS)
                    {
                        salida = k;
                    }
                }
            }

            b[ei, ej] = true;
            if (salida != -1)
            {
                b[camino[salida].r, camino[salida].c] = false;
                Log.AppendLine($"   Variable saliente de la base: ({Org[camino[salida].r]} → {Dst[camino[salida].c]})");
            }

            LogCosto(x, "   Nuevo costo de la red tras la iteración");
            iter++;
        }
        return -1;
    }

    bool BuscarCiclo(List<(int r, int c)> path, bool horizontal, bool[,] b)
    {
        var (r, c) = path[^1];
        var ini = path[0];

        if (path.Count >= 4)
        {
            if (horizontal && r == ini.r) return true;
            if (!horizontal && c == ini.c) return true;
        }

        if (horizontal)
        {
            for (int j = 0; j < N; j++)
            {
                if (j != c && b[r, j] && !path.Contains((r, j)))
                {
                    path.Add((r, j));
                    if (BuscarCiclo(path, false, b)) return true;
                    path.RemoveAt(path.Count - 1);
                }
            }
        }
        else
        {
            for (int i = 0; i < M; i++)
            {
                if (i != r && b[i, c] && !path.Contains((i, c)))
                {
                    path.Add((i, c));
                    if (BuscarCiclo(path, true, b)) return true;
                    path.RemoveAt(path.Count - 1);
                }
            }
        }
        return false;
    }

    public double CostoTotal(double[,] x)
    {
        double t = 0;
        for (int i = 0; i < M; i++)
            for (int j = 0; j < N; j++)
                t += x[i, j] * C[i, j];
        return t;
    }

    // ---------- Comparativa completa de métodos ----------
    public static List<MetodoResultado> CompararTodos(Transporte baseModel)
    {
        var resultados = new List<MetodoResultado>();

        // 1. Esquina Noroeste
        var mENO = baseModel.Clonar();
        var (xENO, bENO) = mENO.EsquinaNoroeste();
        double costoENO = mENO.CostoTotal(xENO);
        var xENOCopy = (double[,])xENO.Clone();
        var bENOCopy = (bool[,])bENO.Clone();
        int itENO = mENO.OptimizarMODI(xENOCopy, bENOCopy);

        // 2. Costo Mínimo
        var mCM = baseModel.Clonar();
        var (xCM, bCM) = mCM.CostoMinimo();
        double costoCM = mCM.CostoTotal(xCM);
        var xCMCopy = (double[,])xCM.Clone();
        var bCMCopy = (bool[,])bCM.Clone();
        int itCM = mCM.OptimizarMODI(xCMCopy, bCMCopy);

        // 3. Vogel
        var mVog = baseModel.Clonar();
        var (xVog, bVog) = mVog.Vogel();
        double costoVog = mVog.CostoTotal(xVog);
        var xVogCopy = (double[,])xVog.Clone();
        var bVogCopy = (bool[,])bVog.Clone();
        int itVog = mVog.OptimizarMODI(xVogCopy, bVogCopy);

        // Solución Óptima
        double costoOptimo = mVog.CostoTotal(xVogCopy);

        resultados.Add(new MetodoResultado
        {
            Metodo = "Esquina Noroeste (ENO)",
            Tipo = "Solución Básica Inicial",
            CostoTotal = costoENO,
            DiferenciaVsOptimo = costoENO - costoOptimo,
            PorcentajeDesviacion = costoOptimo > 0 ? ((costoENO - costoOptimo) / costoOptimo) * 100 : 0,
            IteracionesMODI = itENO,
            Asignacion = xENO,
            Base = bENO,
            Procedimiento = mENO.Log.ToString()
        });

        resultados.Add(new MetodoResultado
        {
            Metodo = "Costo Mínimo (Matriz Mínima)",
            Tipo = "Solución Básica Inicial",
            CostoTotal = costoCM,
            DiferenciaVsOptimo = costoCM - costoOptimo,
            PorcentajeDesviacion = costoOptimo > 0 ? ((costoCM - costoOptimo) / costoOptimo) * 100 : 0,
            IteracionesMODI = itCM,
            Asignacion = xCM,
            Base = bCM,
            Procedimiento = mCM.Log.ToString()
        });

        resultados.Add(new MetodoResultado
        {
            Metodo = "Aproximación de Vogel (VAM)",
            Tipo = "Solución Básica Inicial",
            CostoTotal = costoVog,
            DiferenciaVsOptimo = costoVog - costoOptimo,
            PorcentajeDesviacion = costoOptimo > 0 ? ((costoVog - costoOptimo) / costoOptimo) * 100 : 0,
            IteracionesMODI = itVog,
            Asignacion = xVog,
            Base = bVog,
            Procedimiento = mVog.Log.ToString()
        });

        // Marcar la mejor inicial
        double minInicial = Math.Min(costoENO, Math.Min(costoCM, costoVog));
        foreach (var r in resultados)
        {
            if (Math.Abs(r.CostoTotal - minInicial) < EPS) r.EsMejorInicial = true;
        }

        // Agregar fila óptima
        resultados.Add(new MetodoResultado
        {
            Metodo = "Solución Óptima (MODI u-v)",
            Tipo = "Optimización Exacta",
            CostoTotal = costoOptimo,
            DiferenciaVsOptimo = 0,
            PorcentajeDesviacion = 0,
            IteracionesMODI = 0,
            Asignacion = xVogCopy,
            Base = bVogCopy,
            Procedimiento = mVog.Log.ToString()
        });

        return resultados;
    }
}
