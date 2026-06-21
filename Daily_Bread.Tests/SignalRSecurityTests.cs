using System.Reflection;
using System.Security.Claims;
using Daily_Bread.Hubs;
using Daily_Bread.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Daily_Bread.Tests;

public sealed class SignalRSecurityTests
{
    [Fact]
    public void ChoreHub_Requires_Authentication()
    {
        var attribute = typeof(ChoreHub).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attribute);
        Assert.Null(typeof(ChoreHub).GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public async Task Anonymous_Connection_Is_Rejected()
    {
        var (hub, _, _) = CreateHub(userId: null, role: null);

        await Assert.ThrowsAsync<HubException>(() => hub.OnConnectedAsync());
    }

    [Theory]
    [InlineData("Parent")]
    [InlineData("Admin")]
    public async Task Parent_And_Admin_Connections_Join_Trusted_Parents_Group(string role)
    {
        var (hub, groups, connectionId) = CreateHub("parent-1", role);

        await hub.OnConnectedAsync();

        groups.Verify(
            manager => manager.AddToGroupAsync(connectionId, ChoreHub.ParentsGroup, It.IsAny<CancellationToken>()),
            Times.Once);
        await hub.OnDisconnectedAsync(null);
    }

    [Fact]
    public async Task Child_Connection_Does_Not_Join_Parents_Group()
    {
        var (hub, groups, _) = CreateHub("child-1", "Child");

        await hub.OnConnectedAsync();

        groups.Verify(
            manager => manager.AddToGroupAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        await hub.OnDisconnectedAsync(null);
    }

    [Fact]
    public async Task Help_Alerts_Are_Delivered_Only_To_Parents_Group()
    {
        var fixture = CreateNotificationFixture();

        await fixture.Service.NotifyHelpRequestedAsync(42, "child-1", "Wash dishes", "Victor");

        fixture.Clients.Verify(clients => clients.Group(ChoreHub.ParentsGroup), Times.Once);
        fixture.Clients.Verify(clients => clients.User(It.IsAny<string>()), Times.Never);
        fixture.ParentProxy.Verify(
            proxy => proxy.SendCoreAsync("HelpAlert", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Private_Child_Event_Is_Delivered_Only_To_Target_Child()
    {
        var fixture = CreateNotificationFixture();

        await fixture.Service.NotifyBlessingGrantedAsync("child-1", "Wash dishes", 2.50m, "Parent");

        fixture.Clients.Verify(clients => clients.User("child-1"), Times.Once);
        fixture.Clients.Verify(clients => clients.Group(It.IsAny<string>()), Times.Never);
        fixture.ChildProxy.Verify(
            proxy => proxy.SendCoreAsync("BlessingGranted", It.IsAny<object?[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static (ChoreHub Hub, Mock<IGroupManager> Groups, string ConnectionId) CreateHub(
        string? userId,
        string? role)
    {
        const string connectionId = "connection-1";
        var claims = new List<Claim>();
        if (userId is not null)
        {
            claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
        }
        if (role is not null)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, userId is null ? null : "Test"));
        var context = new Mock<HubCallerContext>();
        context.SetupGet(item => item.ConnectionId).Returns(connectionId);
        context.SetupGet(item => item.UserIdentifier).Returns(userId);
        context.SetupGet(item => item.User).Returns(principal);

        var groups = new Mock<IGroupManager>();
        var hub = new ChoreHub(NullLogger<ChoreHub>.Instance)
        {
            Context = context.Object,
            Groups = groups.Object
        };

        return (hub, groups, connectionId);
    }

    private static NotificationFixture CreateNotificationFixture()
    {
        var parentProxy = new Mock<IClientProxy>();
        var childProxy = new Mock<IClientProxy>();
        var clients = new Mock<IHubClients>();
        clients.Setup(item => item.Group(ChoreHub.ParentsGroup)).Returns(parentProxy.Object);
        clients.Setup(item => item.User("child-1")).Returns(childProxy.Object);

        var context = new Mock<IHubContext<ChoreHub>>();
        context.SetupGet(item => item.Clients).Returns(clients.Object);

        var service = new ChoreNotificationService(
            context.Object,
            NullLogger<ChoreNotificationService>.Instance);

        return new NotificationFixture(service, clients, parentProxy, childProxy);
    }

    private sealed record NotificationFixture(
        ChoreNotificationService Service,
        Mock<IHubClients> Clients,
        Mock<IClientProxy> ParentProxy,
        Mock<IClientProxy> ChildProxy);
}
