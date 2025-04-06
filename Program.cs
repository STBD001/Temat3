using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace CurrencyExchangeAPI
{

    public class ExchangeRatesApiResponse
    {
        public string base_code { get; set; }
        public Dictionary<string, double> rates { get; set; }
        public long time_last_update_unix { get; set; }

        public string GetFormattedUpdateTime()
        {
            return DateTimeOffset.FromUnixTimeSeconds(time_last_update_unix).LocalDateTime.ToString("g");
        }
    }

    public class ExchangeRate
    {
        [Key]
        public int Id { get; set; }

        public required string BaseCurrency { get; set; }
        public required string TargetCurrency { get; set; }
        public required double Rate { get; set; }
        public required DateTime UpdatedAt { get; set; }

        public int CurrencyId { get; set; }
        public Currency Currency { get; set; }

        public override string ToString()
        {
            return $"1 {BaseCurrency} = {Rate:F4} {TargetCurrency} (Aktualizacja: {UpdatedAt:g})";
        }
    }

    public class Currency
    {
        [Key]
        public int Id { get; set; }

        public required string Code { get; set; }
        public string Name { get; set; }

        public ICollection<ExchangeRate> ExchangeRates { get; set; } = new List<ExchangeRate>();

        public override string ToString()
        {
            return $"{Code} - {Name}";
        }
    }

    internal class CurrencyDbContext : DbContext
    {
        public DbSet<Currency> Currencies { get; set; }
        public DbSet<ExchangeRate> ExchangeRates { get; set; }

        public CurrencyDbContext()
        {
            Database.EnsureCreated();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite(@"Data Source=CurrencyExchange.db");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ExchangeRate>()
                .HasOne(e => e.Currency)
                .WithMany(c => c.ExchangeRates)
                .HasForeignKey(e => e.CurrencyId);

            modelBuilder.Entity<Currency>().HasData(
                new Currency { Id = 1, Code = "PLN", Name = "Polski Złoty" },
                new Currency { Id = 2, Code = "USD", Name = "Dolar Amerykański" },
                new Currency { Id = 3, Code = "EUR", Name = "Euro" },
                new Currency { Id = 4, Code = "GBP", Name = "Funt Brytyjski" },
                new Currency { Id = 5, Code = "JPY", Name = "Jen Japoński" },
                new Currency { Id = 6, Code = "CHF", Name = "Frank Szwajcarski" }
            );
        }
    }

    public class CurrencyAPIHandler
    {
        private readonly HttpClient client;
        private readonly CurrencyDbContext dbContext;
        private const string BASE_URL = "https://open.er-api.com/v6/latest/";
        private const double RATE_EPSILON = 0.00001; 

        public CurrencyAPIHandler()
        {
            client = new HttpClient();
            dbContext = new CurrencyDbContext();
        }

        private bool RatesExistInDatabase(string baseCurrency)
        {
            var latestRate = dbContext.ExchangeRates
                .Where(r => r.BaseCurrency == baseCurrency)
                .OrderByDescending(r => r.UpdatedAt)
                .FirstOrDefault();

            if (latestRate != null)
            {
                TimeSpan timeDifference = DateTime.Now - latestRate.UpdatedAt;
                if (timeDifference.TotalHours < 1)
                {
                    Console.WriteLine($"\nKursy dla {baseCurrency} znalezione w bazie danych (aktualizacja: {latestRate.UpdatedAt:g})");
                    return true;
                }
            }
            return false;
        }

        public async Task<List<ExchangeRate>> GetExchangeRatesAsync(string baseCurrency = "PLN")
        {
            if (RatesExistInDatabase(baseCurrency))
            {
                return dbContext.ExchangeRates
                    .Where(r => r.BaseCurrency == baseCurrency)
                    .ToList();
            }

            string apiUrl = $"{BASE_URL}{baseCurrency}";
            try
            {
                Console.WriteLine($"\nPobieranie aktualnych kursów z API dla {baseCurrency}...");
                string response = await client.GetStringAsync(apiUrl);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                ExchangeRatesApiResponse apiResponse = JsonSerializer.Deserialize<ExchangeRatesApiResponse>(response, options);

                if (apiResponse != null)
                {
                    DateTime lastUpdate = DateTimeOffset.FromUnixTimeSeconds(apiResponse.time_last_update_unix).LocalDateTime;
                    await SaveRatesToDatabaseAsync(apiResponse, lastUpdate);

                    return dbContext.ExchangeRates
                        .Where(r => r.BaseCurrency == baseCurrency)
                        .ToList();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nBłąd podczas pobierania kursów walut: {ex.Message}");
            }

            return new List<ExchangeRate>();

        private async Task SaveRatesToDatabaseAsync(ExchangeRatesApiResponse apiResponse, DateTime updateTime)
        {
            try
            {
                var existingRates = dbContext.ExchangeRates
                    .Where(r => r.BaseCurrency == apiResponse.base_code)
                    .ToList();

                var ratesToAdd = new List<ExchangeRate>();
                var ratesToUpdate = new List<ExchangeRate>();
                var currenciesToAdd = new List<Currency>();

                foreach (var rate in apiResponse.rates)
                {
                    var targetCurrency = await dbContext.Currencies.FirstOrDefaultAsync(c => c.Code == rate.Key);
                    if (targetCurrency == null)
                    {
                        targetCurrency = new Currency { Code = rate.Key, Name = rate.Key };
                        currenciesToAdd.Add(targetCurrency);
                    }

                    var existingRate = existingRates.FirstOrDefault(r => r.TargetCurrency == rate.Key);

                    if (existingRate == null)
                    {
                        ratesToAdd.Add(new ExchangeRate
                        {
                            BaseCurrency = apiResponse.base_code,
                            TargetCurrency = rate.Key,
                            Rate = rate.Value,
                            UpdatedAt = updateTime,
                            CurrencyId = targetCurrency.Id
                        });
                    }
                    else if (Math.Abs(existingRate.Rate - rate.Value) > RATE_EPSILON)
                    {
                        existingRate.Rate = rate.Value;
                        existingRate.UpdatedAt = updateTime;
                        ratesToUpdate.Add(existingRate);
                    }
                }

                if (currenciesToAdd.Any())
                {
                    await dbContext.Currencies.AddRangeAsync(currenciesToAdd);
                    await dbContext.SaveChangesAsync();
                }

                if (ratesToAdd.Any())
                {
                    await dbContext.ExchangeRates.AddRangeAsync(ratesToAdd);
                }

                if (ratesToUpdate.Any())
                {
                    dbContext.ExchangeRates.UpdateRange(ratesToUpdate);
                }

                await dbContext.SaveChangesAsync();
                Console.WriteLine($"\nZaktualizowano kursy wymiany: " +
                                  $"{ratesToAdd.Count} nowych, " +
                                  $"{ratesToUpdate.Count} zmienionych");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nBłąd podczas zapisywania do bazy danych: {ex.Message}");
            }
        }

        public async Task DisplayRates(string baseCurrency = "PLN")
        {
            var rates = await GetExchangeRatesAsync(baseCurrency);

            if (!rates.Any())
            {
                Console.WriteLine($"\nNie znaleziono kursów dla waluty {baseCurrency}");
                return;
            }

            Console.WriteLine($"\n=== Kursy wymiany dla {baseCurrency} ===");
            Console.WriteLine($"Ostatnia aktualizacja: {rates.First().UpdatedAt:g}");
            Console.WriteLine(new string('-', 40));

            foreach (var rate in rates.Where(r => new[] { "USD", "EUR", "GBP", "JPY", "CHF" }.Contains(r.TargetCurrency)))
            {
                Console.WriteLine($"{rate.TargetCurrency,-5}: {rate.Rate,10:F4}");
            }
        }

        public async Task CompareSelectedCurrencies(string baseCurrency, string[] targetCurrencies)
        {
            var rates = await GetExchangeRatesAsync(baseCurrency);

            if (rates.Any())
            {
                Console.WriteLine($"\n=== Porównanie kursów {baseCurrency} ===");
                foreach (var currency in targetCurrencies)
                {
                    var rate = rates.FirstOrDefault(r => r.TargetCurrency == currency);
                    Console.WriteLine(rate != null
                        ? $"1 {baseCurrency} = {rate.Rate:F4} {currency}"
                        : $"Nie znaleziono kursu dla waluty {currency}");
                }
            }
            else
            {
                Console.WriteLine($"\nNie znaleziono kursów dla waluty bazowej {baseCurrency}");
            }
        }

        public async Task FilterRatesByValueAsync(string baseCurrency, double minRate)
        {
            await GetExchangeRatesAsync(baseCurrency);

            var filteredRates = dbContext.ExchangeRates
                .Where(r => r.BaseCurrency == baseCurrency && r.Rate > minRate)
                .OrderByDescending(r => r.Rate)
                .ToList();

            Console.WriteLine($"\n=== Waluty o kursie > {minRate:F4} dla {baseCurrency} ===");
            foreach (var rate in filteredRates)
            {
                Console.WriteLine($"{rate.TargetCurrency,-5}: {rate.Rate,10:F4}");
            }
        }

        public void DisplayAllCurrencies()
        {
            Console.WriteLine("\n=== Lista walut w bazie ===");
            Console.WriteLine("ID  | Kod | Nazwa");
            Console.WriteLine(new string('-', 30));
            foreach (var currency in dbContext.Currencies.OrderBy(c => c.Id))
            {
                Console.WriteLine($"{currency.Id,-3} | {currency.Code,-4} | {currency.Name}");
            }
        }

        public void DisplayAllExchangeRates()
        {
            Console.WriteLine("\n=== Lista kursów w bazie ===");
            Console.WriteLine("ID  | Z | Do   | Kurs      | Aktualizacja");
            Console.WriteLine(new string('-', 50));
            foreach (var rate in dbContext.ExchangeRates.OrderBy(r => r.Id))
            {
                Console.WriteLine($"{rate.Id,-3} | {rate.BaseCurrency,-1} | {rate.TargetCurrency,-5} | {rate.Rate,8:F4} | {rate.UpdatedAt:g}");
            }
        }
    }

    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("=== Aplikacja Kursy Walut ===");

            CurrencyAPIHandler currencyHandler = new CurrencyAPIHandler();

            try
            {
                await currencyHandler.DisplayRates("PLN");
                await currencyHandler.CompareSelectedCurrencies("PLN",
                    new[] { "USD", "EUR", "GBP", "JPY", "CHF" });
                await currencyHandler.FilterRatesByValueAsync("PLN", 0.2);
                Console.WriteLine("\n=== Podsumowanie bazy danych ===");
                currencyHandler.DisplayAllCurrencies();
                currencyHandler.DisplayAllExchangeRates();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nWystąpił błąd: {ex.Message}");
            }

            Console.WriteLine("\nNaciśnij dowolny klawisz, aby zakończyć...");
            Console.ReadKey();
        }
    }
}
