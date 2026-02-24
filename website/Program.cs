using EN301549PdfProcessor.Validators;
using EN301549PdfProcessor.Reports;
using EN301549PdfProcessor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<ValidadorAcessibilidadePdf>();
builder.Services.AddSingleton<ServicoAutoTagging>();
builder.Services.AddSingleton<ServicodeRemediacao>();
builder.Services.AddSingleton<GeradorRelatorio>();

var app = builder.Build();

var webRoot = app.Environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
Directory.CreateDirectory(Path.Combine(webRoot, "uploads"));
Directory.CreateDirectory(Path.Combine(webRoot, "outputs"));

app.UseStaticFiles();
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
