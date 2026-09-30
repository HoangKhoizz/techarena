using ban_link_kien_PC.Domain.Auth;
using ban_link_kien_PC.Domain.Builds;
using ban_link_kien_PC.Domain.Chat;
using ban_link_kien_PC.Infrastructure.Gemini;
using ban_link_kien_PC.Domain.Catalog;
using ban_link_kien_PC.Domain.Compatibility;
using ban_link_kien_PC.Domain.Customers;
using ban_link_kien_PC.Domain.Cskh;
using ban_link_kien_PC.Domain.Factories;
using ban_link_kien_PC.Domain.Notifications;
using ban_link_kien_PC.Domain.Orders;
using ban_link_kien_PC.Domain.Payments;
using ban_link_kien_PC.Domain.Promotions;
using ban_link_kien_PC.Domain.Shipping;
using ban_link_kien_PC.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ban_link_kien_PC.CompositionRoot;

public static class ServiceRegistration
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration config)
    {
        var sqlServerConn = config.GetConnectionString("DefaultConnection")
            ?? config.GetConnectionString("SqlServer");
        if (!string.IsNullOrWhiteSpace(sqlServerConn))
        {
            services.AddDbContext<PcStoreDbContext>(o => o.UseSqlServer(sqlServerConn));
        }
        else
        {
            services.AddDbContext<PcStoreDbContext>(o => o.UseInMemoryDatabase("PcStore-InMemory"));
        }

        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));

        return services;
    }

    public static IServiceCollection AddDomainServices(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        services.AddScoped<IComponentCatalogQuery, ComponentCatalogQuery>();
        services.AddScoped<CatalogFilterFacade>();
        services.AddScoped<ProductDetailFacade>();
        services.AddScoped<IProductDetailSpecStrategy, CpuProductDetailSpecStrategy>();
        services.AddScoped<IProductDetailSpecStrategy, MainboardProductDetailSpecStrategy>();
        services.AddScoped<IProductDetailSpecStrategy, RamProductDetailSpecStrategy>();
        services.AddScoped<IProductDetailSpecStrategy, GpuProductDetailSpecStrategy>();
        services.AddScoped<IProductDetailSpecStrategy, PsuProductDetailSpecStrategy>();
        services.AddScoped<ICatalogFilterStrategy, CategoryFilterStrategy>();
        services.AddScoped<ICatalogFilterStrategy, KeywordFilterStrategy>();
        services.AddScoped<ICatalogFilterStrategy, PriceRangeFilterStrategy>();
        services.AddScoped<ICatalogFilterStrategy, BrandFilterStrategy>();
        services.AddScoped<ICatalogFilterStrategy, CpuLineFilterStrategy>();
        services.AddScoped<ICatalogFilterStrategy, RamCapacityFilterStrategy>();
        services.AddScoped<ICatalogFilterStrategy, GpuFilterStrategy>();
        services.AddScoped<ICatalogFilterStrategy, StorageFilterStrategy>();
        services.AddScoped<ICatalogFilterStrategy, AccessoryFilterStrategy>();

        services.AddScoped<ICompatibilityRule, CpuMainboardSocketRule>();
        services.AddScoped<ICompatibilityRule, MainboardRamStandardRule>();
        services.AddScoped<ICompatibilityRule, RamKitQuantityRule>();
        services.AddScoped<ICompatibilityRule, PsuCapacityRule>();

        services.AddScoped<BuildPcFacade>();
        services.AddScoped<CompatibleBuildAssembler>();
        services.AddScoped<BuildConfigurationBuilder>();
        services.AddScoped<BuildEditorFacade>();
        services.AddSingleton<BuildWorkspaceManager>();
        services.AddScoped<ComponentRequestCreator, DefaultComponentRequestCreator>();
        services.AddScoped<IBuildPresetCreator, GamingPresetCreator>();
        services.AddScoped<IBuildPresetCreator, WorkstationPresetCreator>();
        services.AddScoped<IBuildPresetCreator, OfficePresetCreator>();
        services.AddScoped<IBuildPresetCreator, StreamingPresetCreator>();
        services.AddScoped<IBuildPresetCreator, BudgetPresetCreator>();
        services.AddScoped<IEcosystemFactory, AsusEcosystemFactory>();
        services.AddScoped<IEcosystemFactory, MsiEcosystemFactory>();
        services.AddScoped<IEcosystemFactory, AmdPerformanceFactory>();

        services.AddSingleton<CartManager>();
        services.AddScoped<InventoryStockService>();
        services.AddScoped<OrderFulfillmentService>();
        services.AddScoped<OrderTimelineService>();
        services.AddScoped<OrderAssemblyService>();
        services.AddScoped<IShippingService, MockShippingService>();
        services.AddScoped<CreateShipmentService>();
        services.AddScoped<RevenueReportService>();
        services.AddScoped<IOrderConfirmationNotifier, OrderConfirmationNotifier>();
        services.AddScoped<PromoService>();
        services.AddScoped<CheckoutFacade>();
        services.AddScoped<LoyaltyService>();
        services.AddScoped<ReviewEligibilityService>();
        services.AddSingleton<QuestionRateLimiter>();

        services.AddScoped<IStockObserver, InAppStockObserver>();
        services.AddSingleton<IUserNotificationCenter, InMemoryUserNotificationCenter>();
        services.AddScoped<IStockWaitlistService>(sp =>
        {
            var db = sp.GetRequiredService<PcStoreDbContext>();
            var service = new StockWaitlistService(db);
            foreach (var observer in sp.GetServices<IStockObserver>())
                service.RegisterObserver(observer);
            return service;
        });
        services.AddScoped<IOrderStateMachine, OrderStateMachine>();

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<AuthService>();
        services.AddScoped<StaffPortalAuthService>();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddScoped<StaffAuthService>();
        services.AddScoped<PosInvoiceService>();
        services.AddHttpClient("MoMo");
        services.AddScoped<IPaymentGatewayService, MoMoPaymentGatewayService>();
        services.AddScoped<OrderCancellationService>();

        services.AddHttpClient<IGeminiChatService, GeminiChatService>();

        return services;
    }
}

