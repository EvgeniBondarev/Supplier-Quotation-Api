namespace SupplierQuotationApi.Core;

public interface ICurrencyConverter
{
    /// <summary>Переводит сумму в RUB по курсу ЦБ РФ. null — курс валюты неизвестен.</summary>
    Task<decimal?> ToRubAsync(decimal amount, string currency, CancellationToken cancellationToken);
}
