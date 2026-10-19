using Journey.Domain;
using Microsoft.EntityFrameworkCore;

namespace Journey.Data;

public partial class AppDb
{
    private static void ConfigureAccounts(ModelBuilder b)
    {
        b.Entity<User>().HasIndex(x => x.Email).IsUnique();
    }
}
