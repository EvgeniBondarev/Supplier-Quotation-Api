using System.Text.Json.Serialization;
using SupplierQuotationApi.Core;
using SupplierQuotationApi.Core.ProducerAliases;
using SupplierQuotationApi.Infrastructure;
using SupplierQuotationApi.Providers.Armtek;
using SupplierQuotationApi.Providers.Avd;
using SupplierQuotationApi.Providers.Berg;
using SupplierQuotationApi.Providers.MlAuto;
using SupplierQuotationApi.Providers.Moskvorechie;
using SupplierQuotationApi.Providers.Motex;
using SupplierQuotationApi.Providers.FavoritParts;
using SupplierQuotationApi.Providers.ForumAuto;
using SupplierQuotationApi.Providers.ShateM;

// .env → переменные окружения, до создания конфигурации. Уже заданные переменные имеют приоритет.
DotEnv.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<AppOptions>(builder.Configuration.GetSection(AppOptions.Section));
if (!builder.Environment.IsDevelopment() && string.IsNullOrEmpty(builder.Configuration[$"{AppOptions.Section}:ApiKey"]))
    throw new InvalidOperationException("APP__APIKEY не задан: сервис не запускается без ключа вне Development.");

builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddControllers().AddJsonOptions(o =>
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();
builder.Services.AddMemoryCache();

builder.Services.Configure<Studio2DbOptions>(builder.Configuration.GetSection(Studio2DbOptions.Section));
builder.Services.AddSingleton<IProducerAliasService, ProducerAliasService>();
builder.Services.AddSingleton<InflightResultCache>();
builder.Services.AddSingleton<QuotationService>();
builder.Services.AddHttpClient<ICurrencyConverter, CbrCurrencyConverter>(c => c.Timeout = TimeSpan.FromSeconds(10));

builder.Services.AddSingleton(TimeProvider.System);

// Поставщики: одна строка на поставщика.
builder.Services.AddShateM(builder.Configuration);
builder.Services.AddArmtek(builder.Configuration);
builder.Services.AddFavoritParts(builder.Configuration);
builder.Services.AddForumAuto(builder.Configuration);
builder.Services.AddAvd(builder.Configuration);
builder.Services.AddBerg(builder.Configuration);
builder.Services.AddMotex(builder.Configuration);
builder.Services.AddMlAuto(builder.Configuration);
builder.Services.AddMoskvorechie(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapHealthChecks("/health");
app.MapControllers();

app.Run();
