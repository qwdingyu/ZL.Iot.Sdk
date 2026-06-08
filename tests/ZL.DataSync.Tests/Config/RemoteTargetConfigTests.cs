using ZL.DataSync.Config;

namespace ZL.DataSync.Tests.Config;

/// <summary>
/// RemoteTargetConfig 单元测试。
/// </summary>
public class RemoteTargetConfigTests
{
    [Fact]
    public void Default_Name_IsEmpty()
    {
        // Arrange & Act
        var config = new RemoteTargetConfig();

        // Assert
        Assert.Equal(string.Empty, config.Name);
    }

    [Fact]
    public void Default_Type_IsMySql()
    {
        // Arrange & Act
        var config = new RemoteTargetConfig();

        // Assert
        Assert.Equal(TargetType.MySql, config.Type);
    }

    [Fact]
    public void Default_ConnectionString_IsEmpty()
    {
        // Arrange & Act
        var config = new RemoteTargetConfig();

        // Assert
        Assert.Equal(string.Empty, config.ConnectionString);
    }

    [Fact]
    public void Default_TableMappings_IsEmptyDictionary()
    {
        // Arrange & Act
        var config = new RemoteTargetConfig();

        // Assert
        Assert.Empty(config.TableMappings);
    }

    [Fact]
    public void Default_HttpConfig_IsNull()
    {
        // Arrange & Act
        var config = new RemoteTargetConfig();

        // Assert
        Assert.Null(config.HttpConfig);
    }

    [Fact]
    public void Can_Set_All_Properties()
    {
        // Arrange & Act
        var httpConfig = new HttpUploadConfig
        {
            Endpoint = "http://example.com/api",
            TimeoutSeconds = 60,
            DeviceName = "TestDevice",
            Type = "1"
        };

        var config = new RemoteTargetConfig
        {
            Name = "TestTarget",
            Type = TargetType.Http,
            ConnectionString = "server=test;database=test;user=root;password=test;",
            HttpConfig = httpConfig
        };

        // Assert
        Assert.Equal("TestTarget", config.Name);
        Assert.Equal(TargetType.Http, config.Type);
        Assert.Equal("server=test;database=test;user=root;password=test;", config.ConnectionString);
        Assert.NotNull(config.HttpConfig);
        Assert.Equal("http://example.com/api", config.HttpConfig!.Endpoint);
        Assert.Equal(60, config.HttpConfig!.TimeoutSeconds);
        Assert.Equal("TestDevice", config.HttpConfig!.DeviceName);
        Assert.Equal("1", config.HttpConfig!.Type);
    }
}

/// <summary>
/// HttpUploadConfig 单元测试。
/// </summary>
public class HttpUploadConfigTests
{
    [Fact]
    public void Default_Endpoint_IsEmpty()
    {
        // Arrange & Act
        var config = new HttpUploadConfig();

        // Assert
        Assert.Equal(string.Empty, config.Endpoint);
    }

    [Fact]
    public void Default_TableEndpoints_IsEmptyDictionary()
    {
        // Arrange & Act
        var config = new HttpUploadConfig();

        // Assert
        Assert.Empty(config.TableEndpoints);
    }

    [Fact]
    public void Default_TimeoutSeconds_Is30()
    {
        // Arrange & Act
        var config = new HttpUploadConfig();

        // Assert
        Assert.Equal(30, config.TimeoutSeconds);
    }

    [Fact]
    public void Default_Headers_IsEmptyDictionary()
    {
        // Arrange & Act
        var config = new HttpUploadConfig();

        // Assert
        Assert.Empty(config.Headers);
    }

    [Fact]
    public void Default_BodyTemplate_IsNull()
    {
        // Arrange & Act
        var config = new HttpUploadConfig();

        // Assert
        Assert.Null(config.BodyTemplate);
    }

    [Fact]
    public void Default_DeviceName_IsNull()
    {
        // Arrange & Act
        var config = new HttpUploadConfig();

        // Assert
        Assert.Null(config.DeviceName);
    }

    [Fact]
    public void Default_Type_IsNull()
    {
        // Arrange & Act
        var config = new HttpUploadConfig();

        // Assert
        Assert.Null(config.Type);
    }
}
