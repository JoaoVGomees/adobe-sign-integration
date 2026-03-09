// ═══════════════════════════════════════════════════════════════════
// IMPORTS — igual ao "import" do Java
// ═══════════════════════════════════════════════════════════════════
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

// ═══════════════════════════════════════════════════════════════════
// CONFIGURAÇÃO DO SERVIDOR
// Em Java seria o equivalente a configurar o Spring Boot Application
// ═══════════════════════════════════════════════════════════════════
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHttpClient();           // Registra HttpClient (como RestTemplate do Spring)
builder.Services.AddSingleton<TokenStore>(); // Registra TokenStore como Singleton (igual @Scope("singleton") no Spring)
var app = builder.Build();

// ═══════════════════════════════════════════════════════════════════
// CONSTANTES — credenciais do Adobe Sign
// ═══════════════════════════════════════════════════════════════════
const string CLIENT_ID = "ats-c93c69a8-bee1-493b-8132-f8a9bf76acbc";
const string CLIENT_SECRET = "ohE-PMCKf3NA-mCVCxlaN9XpYTuFUSj8";
const string REDIRECT_URI = "https://localhost:5000/oauth";
const string SCOPE = "user_read:self user_login:self agreement_read:self agreement_write:self";
const string ADOBE_AUTH_URL = "https://secure.na3.adobesign.com/public/oauth/v2";
const string ADOBE_TOKEN_URL = "https://secure.na3.adobesign.com/oauth/v2/token";

// ═══════════════════════════════════════════════════════════════════
// ROTA 1: GET /login
// Em Java seria: @GetMapping("/login")
// Redireciona o usuário para a página de login da Adobe
// ═══════════════════════════════════════════════════════════════════
app.MapGet("/login", (HttpContext ctx) =>
{
    // Gera um "state" aleatório para segurança (proteção contra CSRF)
    var state = Guid.NewGuid().ToString("N"); // Guid = UUID do Java
    ctx.Response.Cookies.Append("oauth_state", state, new CookieOptions { HttpOnly = true, Secure = true });

    // Monta os parâmetros da URL (como UriComponentsBuilder do Spring)
    var query = HttpUtility.ParseQueryString(string.Empty);
    query["response_type"] = "code";
    query["client_id"] = CLIENT_ID;
    query["redirect_uri"] = REDIRECT_URI;
    query["scope"] = SCOPE;
    query["state"] = state;

    // Redireciona para a Adobe (como "redirect:" no Spring MVC)
    return Results.Redirect($"{ADOBE_AUTH_URL}?{query}");
});

// ═══════════════════════════════════════════════════════════════════
// ROTA 2: GET /oauth
// Em Java seria: @GetMapping("/oauth")
// Adobe redireciona de volta aqui com um "code" temporário.
// Trocamos esse code pelo access_token.
// ═══════════════════════════════════════════════════════════════════
app.MapGet("/oauth", async (HttpContext ctx, IHttpClientFactory factory, TokenStore store) =>
{
    // Lê os parâmetros que a Adobe mandou na URL
    var code = ctx.Request.Query["code"].ToString();
    var state = ctx.Request.Query["state"].ToString();
    var error = ctx.Request.Query["error"].ToString();

    // Se a Adobe retornou erro, mostra na tela
    if (!string.IsNullOrEmpty(error))
        return Results.Problem($"Erro OAuth: {error}");

    // Valida o "state" para garantir que a requisição é legítima
    var savedState = ctx.Request.Cookies["oauth_state"];
    if (savedState != state)
        return Results.Forbid();

    // Cria um HttpClient (como RestTemplate do Spring)
    var client = factory.CreateClient();

    // Monta o body da requisição POST (como MultiValueMap do Spring)
    var body = new FormUrlEncodedContent(new Dictionary<string, string>
    {
        ["grant_type"] = "authorization_code",
        ["code"] = code,
        ["redirect_uri"] = REDIRECT_URI,
        ["client_id"] = CLIENT_ID,
        ["client_secret"] = CLIENT_SECRET,
    });

    // Faz o POST para trocar o code pelo access_token
    var response = await client.PostAsync(ADOBE_TOKEN_URL, body);
    var raw = await response.Content.ReadAsStringAsync();

    if (!response.IsSuccessStatusCode)
        return Results.Problem($"Falha ao obter token: {raw}");

    // Desserializa o JSON (como ObjectMapper do Jackson no Java)
    var token = JsonSerializer.Deserialize<AdobeTokenResponse>(raw)!;

    // Salva o token no Singleton (como um @Service com estado no Spring)
    store.AccessToken = token.AccessToken!;
    store.ApiAccessPoint = token.ApiAccessPoint!;

    Console.WriteLine($"[OAuth] Token obtido! API: {store.ApiAccessPoint}");

    // Redireciona para o formulário de envio
    return Results.Redirect("/sign");
});

// ═══════════════════════════════════════════════════════════════════
// ROTA 3: GET /sign
// Em Java seria: @GetMapping("/sign")
// Exibe o formulário HTML para o usuário preencher os dados
// ═══════════════════════════════════════════════════════════════════
app.MapGet("/sign", (TokenStore store) =>
{
    // Se não tem token, manda fazer login primeiro
    if (string.IsNullOrEmpty(store.AccessToken))
        return Results.Redirect("/login");

    // Retorna HTML puro (como @ResponseBody com String no Spring)
    var html = """
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset="utf-8"/>
            <title>Adobe Sign — Enviar para Assinatura</title>
            <style>
                body { font-family: sans-serif; max-width: 600px; margin: 40px auto; }
                label { display: block; margin-top: 16px; font-weight: bold; }
                input { width: 100%; padding: 8px; margin-top: 4px; box-sizing: border-box; border: 1px solid #ccc; border-radius: 4px; }
                button { margin-top: 24px; padding: 10px 24px; background: #0070f3; color: white; border: none; border-radius: 4px; cursor: pointer; font-size: 16px; }
                button:hover { background: #0051a8; }
            </style>
        </head>
        <body>
            <h2>Enviar Documento para Assinatura</h2>
            <label>Nome do Signatário</label>
            <input id="name" type="text" placeholder="João Silva" />
            <label>Email do Signatário</label>
            <input id="email" type="email" placeholder="joao@email.com" />
            <label>Título do Documento</label>
            <input id="title" type="text" value="Contrato de Teste" />
            <button onclick="enviar()">Enviar para Assinatura</button>
            <div id="resultado" style="margin-top:24px;"></div>
            <script>
                async function enviar() {
                    const name  = document.getElementById('name').value;
                    const email = document.getElementById('email').value;
                    const title = document.getElementById('title').value;
                    document.getElementById('resultado').innerHTML = 'Processando...';
                    const res = await fetch('/send', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ name, email, title })
                    });
                    const data = await res.json();
                    if (data.signingUrl) {
                        document.getElementById('resultado').innerHTML =
                            '<h3>Documento enviado!</h3>' +
                            '<p>Clique abaixo para assinar agora:</p>' +
                            '<a href="' + data.signingUrl + '" target="_blank" style="display:inline-block;padding:10px 20px;background:#28a745;color:white;border-radius:4px;text-decoration:none;">Assinar Documento</a>' +
                            '<p style="color:gray;font-size:12px;margin-top:8px;">Link enviado por email para ' + email + '</p>';
                    } else {
                        document.getElementById('resultado').innerHTML = '<p style="color:red;">Erro: ' + (data.error ?? JSON.stringify(data)) + '</p>';
                    }
                }
            </script>
        </body>
        </html>
        """;

    return Results.Content(html, "text/html");
});

// ═══════════════════════════════════════════════════════════════════
// ROTA 4: POST /send
// Em Java seria: @PostMapping("/send")
// Recebe os dados do formulário, gera o PDF, faz upload e cria o agreement
// ═══════════════════════════════════════════════════════════════════
app.MapPost("/send", async (HttpContext ctx, IHttpClientFactory factory, TokenStore store) =>
{
    if (string.IsNullOrEmpty(store.AccessToken))
        return Results.Json(new { error = "Não autenticado. Acesse /login primeiro." });

    // Desserializa o JSON do body (como @RequestBody no Spring)
    var body = await JsonSerializer.DeserializeAsync<SendRequest>(ctx.Request.Body);
    if (body is null || string.IsNullOrEmpty(body.Email))
        return Results.Json(new { error = "Dados inválidos." });

    var client = factory.CreateClient();
    var baseUrl = store.ApiAccessPoint.TrimEnd('/');
    var authHeader = $"Bearer {store.AccessToken}";

    // ── PASSO 1: Gera o PDF dinamicamente ────────────────────────────────────
    Console.WriteLine("[PASSO 1] Gerando PDF...");
    var pdfBytes = GerarPdf(body.Title ?? "Contrato", body.Name ?? "Signatário");

    // ── PASSO 2: Faz upload do PDF para a Adobe (transient document) ──────────
    // "Transient" = temporário, fica disponível por 7 dias na Adobe
    Console.WriteLine("[PASSO 2] Fazendo upload do PDF...");
    using var multipart = new MultipartFormDataContent();
    var fileContent = new ByteArrayContent(pdfBytes);
    fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
    multipart.Add(fileContent, "File", "contrato.pdf");
    multipart.Add(new StringContent("contrato.pdf"), "File-Name");
    multipart.Add(new StringContent("NON_PERSISTENT"), "transientDocumentId");

    var uploadReq = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/rest/v6/transientDocuments");
    uploadReq.Headers.Add("Authorization", authHeader);
    uploadReq.Content = multipart;

    var uploadRes = await client.SendAsync(uploadReq);
    var uploadRaw = await uploadRes.Content.ReadAsStringAsync();
    Console.WriteLine($"[PASSO 2] Status: {uploadRes.StatusCode} | Resposta: {uploadRaw}");

    if (!uploadRes.IsSuccessStatusCode)
        return Results.Json(new { error = $"Falha no upload: {uploadRaw}" });

    var uploadData = JsonSerializer.Deserialize<TransientDocumentResponse>(uploadRaw)!;

    // ── PASSO 3: Cria o Agreement (documento para assinar) ────────────────────
    // Agreement = o contrato que será enviado para o signatário
    Console.WriteLine("[PASSO 3] Criando agreement...");
    var agreementPayload = new
    {
        fileInfos = new[] { new { transientDocumentId = uploadData.TransientDocumentId } },
        name = body.Title ?? "Contrato",
        participantSetsInfo = new[]
        {
            new
            {
                memberInfos = new[] { new { email = body.Email, name = body.Name } },
                order = 1,       // ordem de assinatura
                role = "SIGNER"  // papel: assinante
            }
        },
        signatureType = "ESIGN",    // assinatura eletrônica
        state = "IN_PROCESS"        // já inicia o processo de assinatura
    };

    var agreementReq = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/rest/v6/agreements");
    agreementReq.Headers.Add("Authorization", authHeader);
    agreementReq.Content = new StringContent(JsonSerializer.Serialize(agreementPayload), Encoding.UTF8, "application/json");

    var agreementRes = await client.SendAsync(agreementReq);
    var agreementRaw = await agreementRes.Content.ReadAsStringAsync();
    Console.WriteLine($"[PASSO 3] Status: {agreementRes.StatusCode} | Resposta: {agreementRaw}");

    if (!agreementRes.IsSuccessStatusCode)
        return Results.Json(new { error = $"Falha ao criar agreement: {agreementRaw}" });

    var agreementData = JsonSerializer.Deserialize<AgreementResponse>(agreementRaw)!;

    // ── PASSO 4: Busca o link de assinatura ───────────────────────────────────
    Console.WriteLine("[PASSO 4] Buscando link de assinatura...");
    await Task.Delay(4000); // Aguarda a Adobe processar o documento

    var signingReq = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/rest/v6/agreements/{agreementData.Id}/signingUrls");
    signingReq.Headers.Add("Authorization", authHeader);

    var signingRes = await client.SendAsync(signingReq);
    var signingRaw = await signingRes.Content.ReadAsStringAsync();
    Console.WriteLine($"[PASSO 4] Status: {signingRes.StatusCode} | Resposta: {signingRaw}");

    if (!signingRes.IsSuccessStatusCode)
        return Results.Json(new { error = $"Falha ao obter link: {signingRaw}" });

    var signingData = JsonSerializer.Deserialize<SigningUrlResponse>(signingRaw)!;
    var signingUrl = signingData.SigningUrlSetInfos?.FirstOrDefault()?.SigningUrls?.FirstOrDefault()?.EsignUrl;

    if (string.IsNullOrEmpty(signingUrl))
        return Results.Json(new { error = "Não foi possível obter o link de assinatura." });

    Console.WriteLine($"[PASSO 4] Link gerado com sucesso!");
    return Results.Json(new { signingUrl });
});

// Rota raiz redireciona para o formulário
app.MapGet("/", () => Results.Redirect("/sign"));

Console.WriteLine("Servidor rodando em https://localhost:5000");
app.Run("https://localhost:5000");

// ═══════════════════════════════════════════════════════════════════
// GERAÇÃO DO PDF com QuestPDF
// Text Tags: textos especiais que a Adobe converte em campos de assinatura
// Ex: {{Sig_es_:signer1:signature}} vira um campo de assinatura
// ═══════════════════════════════════════════════════════════════════
static byte[] GerarPdf(string titulo, string nomeSignatario)
{
    var document = QuestPDF.Fluent.Document.Create(container =>
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(12));

            page.Content().Column(col =>
            {
                col.Item().AlignCenter().Text(titulo).FontSize(20).Bold();
                col.Item().PaddingVertical(10).LineHorizontal(1);
                col.Item().PaddingTop(10).Text($"Signatário: {nomeSignatario}").Bold();
                col.Item().PaddingTop(5).Text($"Data: {DateTime.Now:dd/MM/yyyy}");
                col.Item().PaddingTop(20).Text(
                    "Este documento é um contrato de teste gerado automaticamente pela integração " +
                    "com o Adobe Acrobat Sign via API REST.");
                col.Item().PaddingTop(20).Text(
                    "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor " +
                    "incididunt ut labore et dolore magna aliqua.");

                // Área de assinatura
                col.Item().PaddingTop(60).Text("Assinatura do Responsável:").Bold();
                col.Item().PaddingTop(5).LineHorizontal(0.5f);

                // Text Tag de assinatura — Adobe Sign converte em campo de assinatura
                // FontColor branco = invisível para humanos, mas Adobe Sign lê normalmente
                col.Item().PaddingTop(5).Text("{{Sig_es_:signer1:signature}}").FontSize(8).FontColor("#FFFFFF");

                col.Item().PaddingTop(5).Text($"Nome: {nomeSignatario}");

                // Text Tag de data — preenchida automaticamente pela Adobe
                col.Item().PaddingTop(5).Text("{{Dte_es_:signer1:date}}").FontSize(8).FontColor("#FFFFFF");
            });
        });
    });

    return document.GeneratePdf();
}

// ═══════════════════════════════════════════════════════════════════
// MODELS — equivalente às classes com @JsonProperty do Jackson no Java
// ═══════════════════════════════════════════════════════════════════

// Armazena o token em memória — equivalente a um @Service Singleton no Spring
public class TokenStore
{
    public string AccessToken { get; set; } = string.Empty; // getter/setter como no Java
    public string ApiAccessPoint { get; set; } = string.Empty;
}

// Equivalente a um DTO com @JsonProperty("name") no Java
public class SendRequest
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
}

public class AdobeTokenResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("api_access_point")] public string? ApiAccessPoint { get; set; }
}

public class TransientDocumentResponse
{
    [JsonPropertyName("transientDocumentId")] public string? TransientDocumentId { get; set; }
}

public class AgreementResponse
{
    [JsonPropertyName("id")] public string? Id { get; set; }
}

public class SigningUrlResponse
{
    [JsonPropertyName("signingUrlSetInfos")] public List<SigningUrlSetInfo>? SigningUrlSetInfos { get; set; }
}

public class SigningUrlSetInfo
{
    [JsonPropertyName("signingUrls")] public List<SigningUrl>? SigningUrls { get; set; }
}

public class SigningUrl
{
    [JsonPropertyName("esignUrl")] public string? EsignUrl { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
}