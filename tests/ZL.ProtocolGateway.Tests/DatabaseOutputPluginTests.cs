using System;
using ZL.ProtocolGateway;
using ZL.ProtocolGateway.Plugins;
using Xunit;

namespace ZL.ProtocolGateway.Tests
{
    public class DatabaseOutputPluginTests
    {
        [Fact]
        public void ResolveProvider_KnownProviders_ReturnsExpectedEnum()
        {
            Assert.Equal(SchemaManager.DatabaseProvider.Sqlite, DatabaseOutputPlugin.ResolveProvider("Sqlite"));
            Assert.Equal(SchemaManager.DatabaseProvider.MySql, DatabaseOutputPlugin.ResolveProvider("MySql"));
            Assert.Equal(SchemaManager.DatabaseProvider.SqlServer, DatabaseOutputPlugin.ResolveProvider("SqlServer"));
            Assert.Equal(SchemaManager.DatabaseProvider.PostgreSQL, DatabaseOutputPlugin.ResolveProvider("PostgreSQL"));
        }

        [Fact]
        public void ResolveProvider_UnsupportedProvider_Throws()
        {
            Assert.Throws<NotSupportedException>(() => DatabaseOutputPlugin.ResolveProvider("oracle"));
        }

        [Theory]
        [InlineData("sqlite", "Sqlite")]
        [InlineData("mysql", "MySql")]
        [InlineData("sqlserver", "SqlServer")]
        [InlineData("postgres", "PostgreSQL")]
        public void NormalizeProvider_ReturnsCanonicalProviderName(string input, string expected)
        {
            Assert.Equal(expected, DatabaseOutputPlugin.NormalizeProvider(input));
        }

        [Fact]
        public void IsAutoCreateDatabaseSupported_OnlySqliteReturnsTrue()
        {
            Assert.True(DatabaseOutputPlugin.IsAutoCreateDatabaseSupported("Sqlite"));
            Assert.False(DatabaseOutputPlugin.IsAutoCreateDatabaseSupported("MySql"));
            Assert.False(DatabaseOutputPlugin.IsAutoCreateDatabaseSupported("SqlServer"));
            Assert.False(DatabaseOutputPlugin.IsAutoCreateDatabaseSupported("PostgreSQL"));
        }
    }
}
