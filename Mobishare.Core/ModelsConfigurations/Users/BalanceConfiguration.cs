using System;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Mobishare.Core.Models.Users;

namespace Mobishare.Core.ModelsConfigurations.Users;

//<summary>
// This class is used to configure the Balances table in the database.
//</summary>
public class BalancesConfiguration : IEntityTypeConfiguration<Balance>
{
    public void Configure(EntityTypeBuilder<Balance> builder)
    {
        builder.ToTable("Balances");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Credit)
                .IsRequired()
                .HasDefaultValue(0);

        builder.Property(x => x.Points)
                .IsRequired()
                .HasDefaultValue(0);

        builder.HasOne(x => x.User)
               .WithOne()
               .HasForeignKey<Balance>(x => x.UserId)
               .IsRequired();
    }
}
