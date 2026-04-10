using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OpenKSeF.Api.Controllers;
using OpenKSeF.Api.Models;
using OpenKSeF.Api.Services;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
using OpenKSeF.Domain.Models;
using OpenKSeF.Domain.Services;
using OpenKSeF.Invoices.Application;
using OpenKSeF.Invoices.Application.Commands.ApproveInvoice;
using OpenKSeF.Invoices.Application.Commands.CreateCorrectionFromOriginal;
using OpenKSeF.Invoices.Application.Commands.CreateFinalInvoiceFromAdvances;
using OpenKSeF.Invoices.Application.Commands.CreateInvoice;
using OpenKSeF.Invoices.Application.Commands.RecordKsefAcceptance;
using OpenKSeF.Invoices.Application.Commands.RecordKsefRejection;
using OpenKSeF.Invoices.Application.Commands.ReopenInvoice;
using OpenKSeF.Invoices.Application.Commands.UpdateInvoiceDraft;
using OpenKSeF.Invoices.Application.Projection;
using OpenKSeF.Invoices.Contracts.Dtos;
using OpenKSeF.Invoices.Domain.Projection;
using OpenKSeF.Invoices.Domain.Validation.Orchestrators;
using OpenKSeF.Invoices.Domain.Integration;
using OpenKSeF.Invoices.Domain.Persistence;
using OpenKSeF.Invoices.Domain.Policies;
using OpenKSeF.Invoices.Infrastructure;
using OpenKSeF.Invoices.Infrastructure.Persistence;
using OpenKSeF.Invoices.Domain.Aggregates;
using OpenKSeF.Invoices.Domain.Entities;
using OpenKSeF.Invoices.Domain.Enums;
using OpenKSeF.Invoices.Domain.Snapshots;
using OpenKSeF.Invoices.Domain.ValueObjects;
using System.Reflection;

namespace OpenKSeF.Api.Tests.Controllers;

public class InvoicesAggregateControllerTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly Guid _ownedTenantId = Guid.NewGuid();

    public InvoicesAggregateControllerTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new ApplicationDbContext(options);
        _currentUser = Substitute.For<ICurrentUserService>();
        _currentUser.UserId.Returns("user-1");

        _db.Tenants.Add(new Tenant
        {
            Id = _ownedTenantId,
            UserId = "user-1",
            Nip = "1234567890",
            DisplayName = "Test tenant",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        _db.SaveChanges();
    }

    [Fact]
    public void Controller_UsesAggregateRouteAndAuthorizeAttribute()
    {
        var controllerType = typeof(InvoicesAggregateController);

        var route = Assert.Single(controllerType.GetCustomAttributes<RouteAttribute>());
        Assert.Equal("api/tenants/{tenantId:guid}/invoices/aggregate", route.Template);
        Assert.Single(controllerType.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Single(controllerType.GetCustomAttributes<ApiControllerAttribute>());
    }

    [Fact]
    public async Task VerifyTenantOwnership_ReturnsTrueOnlyForCurrentUserTenant()
    {
        var controller = new InvoicesAggregateController(
            _db,
            _currentUser,
            Substitute.For<ICreateInvoiceHandler>(),
            Substitute.For<IUpdateInvoiceDraftHandler>(),
            Substitute.For<IApproveInvoiceHandler>(),
            Substitute.For<IReopenInvoiceHandler>(),
            Substitute.For<ICreateCorrectionFromOriginalHandler>(),
            Substitute.For<ICreateFinalInvoiceFromAdvancesHandler>(),
            Substitute.For<IRecordKsefAcceptanceHandler>(),
            Substitute.For<IRecordKsefRejectionHandler>(),
            Substitute.For<IInvoiceReadModelProjector<InvoiceReadDto>>(),
            variant => new InvoicePrintModelProjector(variant),
            Substitute.For<IInvoiceRepository>(),
            new DraftValidationService([], []),
            new KsefSubmissionValidationService([], [], []),
            new InvoiceAggregateValidationContextFactory(_db, Substitute.For<IClock>()),
            new InvoiceAggregateMutationService(),
            Substitute.For<IInvoiceToKsefPayloadMapper>(),
            Substitute.For<IClock>());

        var verifyMethod = typeof(InvoicesAggregateController)
            .GetMethod("VerifyTenantOwnership", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(verifyMethod);

        var ownedTask = Assert.IsType<Task<bool>>(verifyMethod!.Invoke(controller, [_ownedTenantId]));
        var foreignTask = Assert.IsType<Task<bool>>(verifyMethod.Invoke(controller, [Guid.NewGuid()]));

        Assert.True(await ownedTask);
        Assert.False(await foreignTask);
    }

    [Fact]
    public void AddInvoiceApplication_RegistersAggregateControllerDependencies()
    {
        var services = new ServiceCollection();
        services.AddInvoiceApplication();
        services.AddInvoiceInfrastructure();
        services.AddDbContext<ApplicationDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));

        using var provider = services.BuildServiceProvider();

        Assert.IsType<CreateInvoiceHandler>(provider.GetRequiredService<ICreateInvoiceHandler>());
        Assert.IsType<UpdateInvoiceDraftHandler>(provider.GetRequiredService<IUpdateInvoiceDraftHandler>());
        Assert.IsType<ApproveInvoiceHandler>(provider.GetRequiredService<IApproveInvoiceHandler>());
        Assert.IsType<ReopenInvoiceHandler>(provider.GetRequiredService<IReopenInvoiceHandler>());
        Assert.IsType<CreateCorrectionFromOriginalHandler>(provider.GetRequiredService<ICreateCorrectionFromOriginalHandler>());
        Assert.IsType<CreateFinalInvoiceFromAdvancesHandler>(provider.GetRequiredService<ICreateFinalInvoiceFromAdvancesHandler>());
        Assert.IsType<RecordKsefAcceptanceHandler>(provider.GetRequiredService<IRecordKsefAcceptanceHandler>());
        Assert.IsType<RecordKsefRejectionHandler>(provider.GetRequiredService<IRecordKsefRejectionHandler>());
        Assert.IsType<InvoiceReadDtoProjector>(provider.GetRequiredService<IInvoiceReadModelProjector<InvoiceReadDto>>());
        Assert.IsType<EfInvoiceRepository>(provider.GetRequiredService<IInvoiceRepository>());
        Assert.IsType<IssuedInvoiceAggregateMapper>(provider.GetRequiredService<IssuedInvoiceAggregateMapper>());

        var printFactory = provider.GetRequiredService<Func<PrintVariant, InvoicePrintModelProjector>>();
        Assert.IsType<InvoicePrintModelProjector>(printFactory(PrintVariant.Standard));
    }

    [Fact]
    public async Task List_FiltersByStatus_AndProjectsAggregateDtos()
    {
        var repository = new EfInvoiceRepository(_db, new IssuedInvoiceAggregateMapper());
        await repository.SaveAsync(MakeDraftInvoice(_ownedTenantId, "FV/DRAFT"));
        await repository.SaveAsync(MakeAcceptedInvoice(_ownedTenantId, "FV/ACCEPTED"));

        var result = await CreateController(repository).List(_ownedTenantId, ["Draft"], null, null, null, null, 1, 25);

        var ok = Assert.IsType<OkObjectResult>(result);
        var page = Assert.IsType<PagedResult<InvoiceReadDto>>(ok.Value);
        var item = Assert.Single(page.Items);
        Assert.Equal("Draft", item.Status);
        Assert.Equal("VatInvoice", item.Kind);
        Assert.Equal("NotPlanned", item.KsefSubmissionState);
    }

    [Fact]
    public async Task List_ReturnsBadRequest_ForUnknownStatusFilter()
    {
        var result = await CreateController().List(_ownedTenantId, ["Nope"], null, null, null, null, 1, 25);

        var badRequest = Assert.IsType<BadRequestObjectResult>(result);
        var error = Assert.IsType<RuleCodeErrorResponse>(badRequest.Value);
        Assert.Equal("INV-VAL-003", error.Code);
    }

    [Fact]
    public async Task Get_ReturnsNotFound_WhenTenantIsNotOwned()
    {
        var foreignTenantId = Guid.NewGuid();
        _db.Tenants.Add(new Tenant
        {
            Id = foreignTenantId,
            UserId = "user-2",
            Nip = "2222222222",
            DisplayName = "Foreign tenant",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var repository = new EfInvoiceRepository(_db, new IssuedInvoiceAggregateMapper());
        var invoice = MakeDraftInvoice(foreignTenantId, "FV/FOREIGN");
        await repository.SaveAsync(invoice);

        var result = await CreateController(repository).Get(foreignTenantId, invoice.Id.Value);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task GetPrint_DuplicateBeforeAcceptance_ReturnsConflict()
    {
        var repository = new EfInvoiceRepository(_db, new IssuedInvoiceAggregateMapper());
        var invoice = MakeDraftInvoice(_ownedTenantId, "FV/DUP");
        await repository.SaveAsync(invoice);

        var result = await CreateController(repository).GetPrint(_ownedTenantId, invoice.Id.Value, nameof(PrintVariant.Duplicate));

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var error = Assert.IsType<RuleCodeErrorResponse>(conflict.Value);
        Assert.Equal("IMM-003", error.Code);
    }

    private InvoicesAggregateController CreateController(IInvoiceRepository? invoiceRepository = null)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc));

        return new InvoicesAggregateController(
            _db,
            _currentUser,
            Substitute.For<ICreateInvoiceHandler>(),
            Substitute.For<IUpdateInvoiceDraftHandler>(),
            Substitute.For<IApproveInvoiceHandler>(),
            Substitute.For<IReopenInvoiceHandler>(),
            Substitute.For<ICreateCorrectionFromOriginalHandler>(),
            Substitute.For<ICreateFinalInvoiceFromAdvancesHandler>(),
            Substitute.For<IRecordKsefAcceptanceHandler>(),
            Substitute.For<IRecordKsefRejectionHandler>(),
            new InvoiceReadDtoProjector(),
            variant => new InvoicePrintModelProjector(variant),
            invoiceRepository ?? new EfInvoiceRepository(_db, new IssuedInvoiceAggregateMapper()),
            new DraftValidationService([], []),
            new KsefSubmissionValidationService([], [], []),
            new InvoiceAggregateValidationContextFactory(_db, clock),
            new InvoiceAggregateMutationService(),
            Substitute.For<IInvoiceToKsefPayloadMapper>(),
            clock);
    }

    private static Invoice MakeDraftInvoice(Guid tenantId, string documentNumber)
    {
        var invoice = Invoice.Draft(
            InvoiceId.New(),
            new TenantId(tenantId),
            DocumentKind.VatInvoice,
            new SellerSnapshot(new PartyName("Seller"), new Nip("1234567890")),
            new BuyerSnapshot(new PartyName("Buyer"), BuyerKind.Business, new Nip("9876543210")),
            CurrencyCode.Pln,
            new DateTime(2026, 4, 10),
            KsefSubmissionRequirement.Required,
            new DocumentNumber(documentNumber));

        invoice.AddLine(InvoiceLine.Create(
            1,
            "Service",
            1m,
            new Money(100m, CurrencyCode.Pln),
            PricingMode.Net,
            VatRate.OfPercentage(new Percentage(23))));
        invoice.RecalculateTotals();

        return invoice;
    }

    private static Invoice MakeAcceptedInvoice(Guid tenantId, string documentNumber)
    {
        var invoice = MakeDraftInvoice(tenantId, documentNumber);
        invoice.Approve(new DateTime(2026, 4, 10, 9, 0, 0, DateTimeKind.Utc));
        invoice.SubmitToKsef(new DateTime(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc));
        invoice.AcceptByKsef(
            new KsefIdentifiers("KSEF-123", "REF-123"),
            new DateTime(2026, 4, 10, 11, 0, 0, DateTimeKind.Utc));

        return invoice;
    }

    public void Dispose() => _db.Dispose();
}
