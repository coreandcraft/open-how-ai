using Journey.Domain;
using Microsoft.EntityFrameworkCore;

namespace Journey.Data;

public partial class AppDb
{
    private static void ConfigureNotifications(ModelBuilder b)
    {
        b.Entity<Notification>().HasIndex(x => new { x.UserId, x.Read });
    }
}
