using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OpenKSeF.Api.Controllers;
using OpenKSeF.Domain.Data;
using OpenKSeF.Domain.Entities;
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
            variant => new InvoicePrintModelProjector(variant));

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

        var printFactory = provider.GetRequiredService<Func<PrintVariant, InvoicePrintModelProjector>>();
        Assert.IsType<InvoicePrintModelProjector>(printFactory(PrintVariant.Standard));
    }

    public void Dispose() => _db.Dispose();
}
