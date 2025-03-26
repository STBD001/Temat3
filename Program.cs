using System.Net.Http;
using System.Text.Json;

namespace CurrencyExchangeAPI
{
    public class ExchangeRates
    {
        public string base_code { get; set; }
        public Dictionary<string, decimal> rates { get; set; }
        public long time_last_update_unix { get; set; }

        public string GetFormattedUpdateTime()
        {
            return DateTimeOffset.FromUnixTimeSeconds(time_last_update_unix).LocalDateTime.ToString();
        }

        public void DisplayRates()
        {
            Console.WriteLine($"Kursy wymiany dla waluty bazowej: {base_code}");
            Console.WriteLine($"Ostatnia aktualizacja: {GetFormattedUpdateTime()}");
            Console.WriteLine("----------------------------");

            foreach (var rate in rates)
            {
                Console.WriteLine($"{rate.Key}: {rate.Value:F4}");
            }
        }
    }

    public class CurrencyAPIHandler
    {
        private HttpClient client;
        private const string BASE_URL = "https://open.er-api.com/v6/latest/";

        public CurrencyAPIHandler()
        {
            client = new HttpClient();
        }

        public async Task<ExchangeRates> GetExchangeRatesAsync(string baseCurrency = "PLN")
        {
            string apiUrl = $"{BASE_URL}{baseCurrency}";

            try
            {
                string response = await client.GetStringAsync(apiUrl);
                ExchangeRates exchangeRates = JsonSerializer.Deserialize<ExchangeRates>(response);
                return exchangeRates;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd podczas pobierania kursów walut: {ex.Message}");
                return null;
            }
        }

        public async Task CompareSelectedCurrencies(string baseCurrency, string[] targetCurrencies)
        {
            var exchangeRates = await GetExchangeRatesAsync(baseCurrency);

            if (exchangeRates != null)
            {
                Console.WriteLine($"Kursy wymiany dla {baseCurrency}:");
                foreach (var currency in targetCurrencies)
                {
                    if (exchangeRates.rates.TryGetValue(currency, out decimal rate))
                    {
                        Console.WriteLine($"1 {baseCurrency} = {rate:F4} {currency}");
                    }
                    else
                    {
                        Console.WriteLine($"Nie znaleziono kursu dla waluty {currency}");
                    }
                }
            }
        }
    }

    internal class Program
    {
        static async Task Main(string[] args)
        {
            CurrencyAPIHandler currencyHandler = new CurrencyAPIHandler();

            try
            {
                var usdRates = await currencyHandler.GetExchangeRatesAsync("PLN");
                usdRates?.DisplayRates();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Błąd: {ex.Message}");
            }

            Console.WriteLine("\n--- Porównanie wybranych walut ---");

            await currencyHandler.CompareSelectedCurrencies("PLN",
                new[] { "USD", "EUR", "GBP", "JPY", "CHF" });
        }
    }
}