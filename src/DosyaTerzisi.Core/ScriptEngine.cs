using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DosyaTerzisi.Core;

/// <summary>Bir betik ifadesinin sonucu: değer ya da (Türkçe) hata metni.</summary>
public readonly record struct ScriptResult(string Value, string? Error)
{
    public static ScriptResult Failure(string message) => new("", message);
}

/// <summary>
/// Bir öğe için hazırlanmış betik ifadeleri (normal durumda bir tane; düzenli ifadede eşleşme başına bir tane)
/// ve sonuçlardan yeni adı kuran işlev. Planlayıcı tüm öğelerin ifadelerini toplayıp tek seferde değerlendirir.
/// </summary>
public sealed class ScriptJob(IReadOnlyList<string> expressions, Func<IReadOnlyList<ScriptResult>, (string Name, string? Error)> finish)
{
    public IReadOnlyList<string> Expressions { get; } = expressions;

    public (string Name, string? Error) Finish(IReadOnlyList<ScriptResult> results) => finish(results);
}

/// <summary>
/// Kullanıcının betik dosyasını (<c>Script.vbs</c> ya da <c>Script.js</c>) kullanarak ifadeleri değerlendirir.
/// Bir çağrıdaki tüm ifadeler TEK bir <c>cscript.exe</c> sürecinde çalışır; ifadeler komut satırına değil girdi dosyasına yazılır.
/// </summary>
public sealed class ScriptEngine
{
    public const string VBScript = "vbscript";
    public const string JScript = "jscript";

    private int _launches;

    /// <param name="folder">Betik klasörü; null = <c>%AppData%\DosyaTerzisi</c>.</param>
    public ScriptEngine(string? folder = null)
    {
        Folder = string.IsNullOrWhiteSpace(folder) ? DefaultFolder : folder;
    }

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DosyaTerzisi");

    public string Folder { get; }

    /// <summary>Bir değerlendirmenin en çok ne kadar süreceği; aşılırsa süreç öldürülür.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Son değerlendirmenin geçici klasörü (silinmiş olmalı; testler için).</summary>
    public string? LastWorkFolder { get; private set; }

    /// <summary>Şimdiye kadar başlatılan cscript süreci sayısı (testler için).</summary>
    public int LaunchCount => Volatile.Read(ref _launches);

    private string VbsPath => Path.Combine(Folder, "Script.vbs");

    private string JsPath => Path.Combine(Folder, "Script.js");

    /// <summary>Script.js var ve Script.vbs yoksa JScript, aksi halde VBScript.</summary>
    public string Language => File.Exists(JsPath) && !File.Exists(VbsPath) ? JScript : VBScript;

    public string ScriptPath => Language == JScript ? JsPath : VbsPath;

    public bool Exists => File.Exists(ScriptPath);

    /// <summary>Betik dosyası yoksa klasörü ve örnek içerikli dosyayı oluşturur.</summary>
    public string EnsureScriptFile()
    {
        Directory.CreateDirectory(Folder);
        string path = ScriptPath;
        if (!File.Exists(path))
            File.WriteAllText(path, SampleScript, new UTF8Encoding(true));
        return path;
    }

    private const string SampleScript = """
        ' Dosya Terzisi betik dosyası (VBScript).
        ' "Betik" kutusu işaretliyken "Yeni ad" alanı bir ifade olarak değerlendirilir.
        ' Aşağıda tanımlanan işlevleri ifadede kullanabilirsiniz, örneğin:  Buyuk("\1") & "." & "\2"
        ' Dosya adındaki \1, \0, \e gibi değişkenler metin olarak yerine konur; metin için çift tırnak kullanın.

        ' Metni büyük harfe çevirir.
        Function Buyuk(s)
            Buyuk = UCase(s)
        End Function

        ' Metnin ilk n karakterini verir.
        Function Bas(s, n)
            Bas = Left(s, n)
        End Function

        ' Metnin son n karakterini verir.
        Function Son(s, n)
            Son = Right(s, n)
        End Function

        ' Sayıyı en az n basamaklı, başı sıfırlı yazar:  Sifirla("\1", 3)  ->  007
        Function Sifirla(s, n)
            Sifirla = Right(String(n, "0") & s, IIf(Len(s) > n, Len(s), n))
        End Function

        Function IIf(c, a, b)
            If c Then IIf = a Else IIf = b
        End Function
        """;

    /// <summary>Tüm ifadeleri tek süreçte değerlendirir. Asla özel durum fırlatmaz; her ifade için sonuç ya da hata döner.</summary>
    public IReadOnlyList<ScriptResult> Evaluate(IReadOnlyList<string> expressions)
    {
        if (expressions.Count == 0) return [];

        string work = Path.Combine(Path.GetTempPath(), "dosyaterzisi-script-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(work);
            LastWorkFolder = work;
            string language = Language;
            bool js = language == JScript;
            string ext = js ? ".js" : ".vbs";

            var utf16 = new UnicodeEncoding(false, true);
            string input = Path.Combine(work, "in.txt");
            string output = Path.Combine(work, "out.txt");
            string user = Path.Combine(work, "user.txt");
            string driver = Path.Combine(work, "driver" + ext);

            var lines = new StringBuilder();
            foreach (string expression in expressions) lines.Append(EscapeLine(expression)).Append('\n');
            File.WriteAllText(input, lines.ToString(), utf16);
            File.WriteAllText(user, ReadUserScript() + "\r\n", utf16);
            File.WriteAllText(driver, Driver(js, input, output, user), utf16);

            if (!Run(driver, language, work, out string? failure))
                return Fill(expressions.Count, failure!);

            if (!File.Exists(output))
                return Fill(expressions.Count, "Betik çalıştırılamadı (cscript sonuç üretmedi).");

            string text = File.ReadAllText(output, utf16);
            var parts = text.Split('\n');
            var results = new List<ScriptResult>(expressions.Count);
            for (int i = 0; i < expressions.Count; i++)
            {
                if (i >= parts.Length - 1)
                {
                    results.Add(ScriptResult.Failure("Betik hatası: sonuç alınamadı."));
                    continue;
                }
                string line = UnescapeLine(parts[i]);
                results.Add(line.Length > 0 && line[0] == '\u0001'
                    ? ScriptResult.Failure("Betik hatası: " + (line[1..].StartsWith("HATA: ", StringComparison.Ordinal) ? line[7..] : line[1..]))
                    : new ScriptResult(line, null));
            }
            return results;
        }
        catch (Exception ex)
        {
            return Fill(expressions.Count, "Betik çalıştırılamadı: " + ex.Message);
        }
        finally
        {
            try { if (Directory.Exists(work)) Directory.Delete(work, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static IReadOnlyList<ScriptResult> Fill(int count, string message) =>
        Enumerable.Repeat(ScriptResult.Failure(message), count).ToList();

    private bool Run(string driver, string language, string workDir, out string? failure)
    {
        failure = null;
        string cscript = Path.Combine(Environment.SystemDirectory, "cscript.exe");
        var psi = new ProcessStartInfo(File.Exists(cscript) ? cscript : "cscript.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workDir,
        };
        psi.ArgumentList.Add("//nologo");
        psi.ArgumentList.Add("//B");
        psi.ArgumentList.Add("//E:" + language);
        psi.ArgumentList.Add(driver);

        Interlocked.Increment(ref _launches);
        using var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            failure = "Betik çalıştırılamadı (cscript.exe başlatılamadı): " + ex.Message;
            return false;
        }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit((int)Math.Min(Timeout.TotalMilliseconds, int.MaxValue)))
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { }
            try { process.WaitForExit(2000); } catch (Exception) { }
            failure = "Betik zaman aşımına uğradı";
            return false;
        }
        process.WaitForExit();
        return true;
    }

    private string ReadUserScript()
    {
        string path = ScriptPath;
        if (!File.Exists(path)) return "";

        byte[] bytes = File.ReadAllBytes(path);
        var utf8Strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
        try
        {
            // BOM'lu UTF-8/16/32 dosyaları doğru okunur; BOM'suzlar UTF-8 sayılır.
            using var reader = new StreamReader(new MemoryStream(bytes), utf8Strict, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
        catch (DecoderFallbackException)
        {
            // UTF-8 değil: Windows'un ANSI kod sayfası (Not Defteri'nin eski "ANSI" kaydı).
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage).GetString(bytes);
        }
    }

    // ---- Girdi/çıktı satırı kaçışı ---------------------------------------------------------------------------------

    internal static string EscapeLine(string text) =>
        text.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");

    internal static string UnescapeLine(string text)
    {
        if (!text.Contains('\\')) return text;
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\\' && i + 1 < text.Length)
            {
                char n = text[++i];
                sb.Append(n switch { 'n' => '\n', 'r' => '\r', 't' => '\t', _ => n });
            }
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    // ---- Sürücü betikleri ------------------------------------------------------------------------------------------

    private static string Driver(bool js, string input, string output, string user)
    {
        string template = js ? JsDriver : VbsDriver;
        string Lit(string path) => js ? path.Replace("\\", "\\\\") : path;
        return template
            .Replace("%%IN%%", Lit(input))
            .Replace("%%OUT%%", Lit(output))
            .Replace("%%USER%%", Lit(user));
    }

    private const string VbsDriver = """
        On Error Resume Next
        Dim FR_fso, FR_ts, FR_src, FR_all, FR_parts, FR_n, FR_i, FR_out(), FR_loadErr, FR_expr, FR_v, FR_r, FR_w

        Function FR_Unesc(s)
            Dim k, ch, o
            o = ""
            k = 1
            Do While k <= Len(s)
                ch = Mid(s, k, 1)
                If ch = "\" And k < Len(s) Then
                    k = k + 1
                    ch = Mid(s, k, 1)
                    If ch = "n" Then
                        ch = vbLf
                    ElseIf ch = "r" Then
                        ch = vbCr
                    ElseIf ch = "t" Then
                        ch = vbTab
                    End If
                End If
                o = o & ch
                k = k + 1
            Loop
            FR_Unesc = o
        End Function

        Function FR_Esc(s)
            s = Replace(s, "\", "\\")
            s = Replace(s, vbCr, "\r")
            s = Replace(s, vbLf, "\n")
            s = Replace(s, vbTab, "\t")
            FR_Esc = s
        End Function

        Function FR_Describe()
            If Err.Description <> "" Then
                FR_Describe = Err.Description
            Else
                FR_Describe = "Hata " & Err.Number
            End If
        End Function

        Set FR_fso = CreateObject("Scripting.FileSystemObject")
        Set FR_ts = FR_fso.OpenTextFile("%%IN%%", 1, False, -1)
        FR_all = FR_ts.ReadAll
        FR_ts.Close
        FR_parts = Split(FR_all, vbLf)
        FR_n = UBound(FR_parts)

        FR_loadErr = ""
        Set FR_ts = FR_fso.OpenTextFile("%%USER%%", 1, False, -1)
        FR_src = FR_ts.ReadAll
        FR_ts.Close
        Err.Clear
        ExecuteGlobal FR_src
        If Err.Number <> 0 Then FR_loadErr = "betik dosyası: " & FR_Describe()

        ReDim FR_out(FR_n)
        For FR_i = 0 To FR_n - 1
            Err.Clear
            If FR_loadErr <> "" Then
                FR_r = Chr(1) & "HATA: " & FR_loadErr
            Else
                FR_expr = FR_Unesc(FR_parts(FR_i))
                FR_v = Empty
                FR_v = Eval(FR_expr)
                If Err.Number <> 0 Then
                    FR_r = Chr(1) & "HATA: " & FR_Describe()
                ElseIf IsNull(FR_v) Then
                    FR_r = ""
                Else
                    FR_r = CStr(FR_v)
                    If Err.Number <> 0 Then FR_r = Chr(1) & "HATA: " & FR_Describe()
                End If
            End If
            FR_out(FR_i) = FR_Esc(FR_r)
        Next

        Set FR_w = FR_fso.CreateTextFile("%%OUT%%", True, True)
        FR_w.Write Join(FR_out, vbLf)
        FR_w.Close
        """;

    private const string JsDriver = """
        var FR_fso = new ActiveXObject("Scripting.FileSystemObject");
        function FR_unesc(s) {
            var o = [];
            for (var k = 0; k < s.length; k++) {
                var ch = s.charAt(k);
                if (ch == "\\" && k < s.length - 1) {
                    k++;
                    ch = s.charAt(k);
                    if (ch == "n") ch = "\n"; else if (ch == "r") ch = "\r"; else if (ch == "t") ch = "\t";
                }
                o.push(ch);
            }
            return o.join("");
        }
        function FR_esc(s) {
            return s.replace(/\\/g, "\\\\").replace(/\r/g, "\\r").replace(/\n/g, "\\n").replace(/\t/g, "\\t");
        }
        function FR_text(p) {
            var f = FR_fso.OpenTextFile(p, 1, false, -1);
            try { return f.AtEndOfStream ? "" : f.ReadAll(); } finally { f.Close(); }
        }
        function FR_describe(e) {
            var m = e && (e.description || e.message);
            return m ? String(m) : String(e);
        }
        function FR_run(FR_expr) { return eval(FR_expr); }

        var FR_parts = FR_text("%%IN%%").split("\n");
        var FR_n = FR_parts.length - 1;
        var FR_loadErr = null;
        try { eval(FR_text("%%USER%%")); } catch (FR_e) { FR_loadErr = "betik dosyası: " + FR_describe(FR_e); }

        var FR_out = [];
        for (var FR_i = 0; FR_i < FR_n; FR_i++) {
            var FR_r;
            if (FR_loadErr !== null) {
                FR_r = "\u0001HATA: " + FR_loadErr;
            } else {
                try {
                    var FR_v = FR_run(FR_unesc(FR_parts[FR_i]));
                    FR_r = (FR_v === undefined || FR_v === null) ? "" : String(FR_v);
                } catch (FR_e2) {
                    FR_r = "\u0001HATA: " + FR_describe(FR_e2);
                }
            }
            FR_out.push(FR_esc(FR_r));
        }
        var FR_w = FR_fso.CreateTextFile("%%OUT%%", true, true);
        FR_w.Write(FR_out.join("\n") + "\n");
        FR_w.Close();
        """;
}

/// <summary>
/// Yeni ad şablonunu bir betik ifadesine çevirir: <c>\1</c>, <c>\0</c>, <c>\e</c>, <c>?</c> gibi değişkenler metin olarak yerine konur.
/// Çift tırnak içinde değer o dilin kuralıyla kaçışlanır; tırnak dışında yalnızca sayılar olduğu gibi, diğer değerler
/// tırnaklı metin olarak konur. Böylece dosya adı hiçbir zaman ifade yapısını bozamaz.
/// Desteklenmeyenler (olduğu gibi bırakılır): <c>\u \U \l \L \E \Q</c> ve <c>*</c> (çarpma işaretidir).
/// </summary>
public static class ScriptTemplate
{
    private static readonly Regex Number = new(@"^-?(0|[1-9][0-9]{0,14})(\.[0-9]{1,15})?$", RegexOptions.CultureInvariant);
    private static readonly Regex LeadingZeroNumber = new(@"^-?[0-9]{1,15}(\.[0-9]{1,15})?$", RegexOptions.CultureInvariant);

    /// <param name="tags">true ise <c>&lt;Grup.Ad&gt;</c> etiketleri (ctx.Tags ile) yerine konur.</param>
    public static string Build(string template, string language, ExpansionContext ctx, bool tags = false)
    {
        bool js = language == ScriptEngine.JScript;
        var sb = new StringBuilder();
        char quote = '\0';

        void Insert(string value)
        {
            if (quote != '\0') sb.Append(EscapeInString(value, js, quote));
            else if (Number.IsMatch(value)) sb.Append(value);
            else if (js && LeadingZeroNumber.IsMatch(value)) sb.Append("parseFloat(\"").Append(value).Append("\")");
            else if (!js && LeadingZeroNumber.IsMatch(value)) sb.Append(value);
            else sb.Append('"').Append(EscapeInString(value, js, '"')).Append('"');
        }

        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];

            if (c == '\\' && i + 1 < template.Length)
            {
                char code = template[i + 1];
                if (code == 'P')
                {
                    string dir = ctx.Entry.Directory;
                    Insert(dir.Length == 0 || dir.EndsWith('\\') || dir.EndsWith('/') ? dir : dir + "\\");
                    i++;
                    continue;
                }
                if (IsValueCode(code))
                {
                    Insert(ReplacementTemplate.Expand("\\" + code, ctx));
                    i++;
                    continue;
                }
                if (code == '#' && i + 2 < template.Length && "YymdHMSpIaAbBjwxXc".Contains(template[i + 2]))
                {
                    Insert(ReplacementTemplate.Expand("\\#" + template[i + 2], ctx));
                    i += 2;
                    continue;
                }

                if (js)
                {
                    // JScript'te \" gibi kaçışlar betiğin kendisinindir: iki karakter olduğu gibi geçer.
                    sb.Append(c).Append(code);
                    i++;
                }
                else
                {
                    sb.Append(c);
                }
                continue;
            }

            if (c == '?')
            {
                int end = i;
                while (end < template.Length && template[end] == '?') end++;
                Insert(ReplacementTemplate.Expand(template[i..end], ctx));
                i = end - 1;
                continue;
            }

            if (c == '<' && tags && ctx.Tags is not null && TagToken.Match(template, i) is { Success: true } tag)
            {
                Insert(ReplacementTemplate.Expand(tag.Value, ctx));
                i += tag.Length - 1;
                continue;
            }

            if (js)
            {
                if (quote == '\0' && (c == '"' || c == '\'')) quote = c;
                else if (c == quote) quote = '\0';
            }
            else if (c == '"')
            {
                quote = quote == '\0' ? '"' : '\0';
            }
            sb.Append(c);
        }

        return sb.ToString();
    }

    private static readonly Regex TagToken = new(@"\G<[A-Za-z0-9]+\.[A-Za-z0-9\-]+(:[^>]*)?>", RegexOptions.CultureInvariant);

    private static bool IsValueCode(char code) =>
        code is >= '0' and <= '9' || "tefF;:YymdHMSpIaAbBjwxXc".Contains(code);

    private static string EscapeInString(string value, bool js, char quote)
    {
        var sb = new StringBuilder(value.Length + 8);
        foreach (char c in value)
        {
            if (js)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\'': sb.Append("\\'"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ' || c == '\u2028' || c == '\u2029')
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(c);
                        break;
                }
            }
            else if (c == '"')
                sb.Append("\"\"");
            else if (c < ' ')
                sb.Append("\" & Chr(").Append((int)c).Append(") & \"");
            else
                sb.Append(c);
        }
        return sb.ToString();
    }
}
