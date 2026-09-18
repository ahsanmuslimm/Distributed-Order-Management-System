using Microsoft.AspNetCore.Http;
using Orders.Service.Infrastructure;
using Orders.Service.Middleware;
using Xunit;

namespace Orders.Service.Tests;

/// <summary>
/// Tests for CorrelationId middleware
/// Verifies extraction, storage, and propagation
/// </summary>
public class CorrelationIdMiddlewareTests
{
    // ========================================================================
    // Test: Extract from Request Header
    // ========================================================================
    [Fact]
    public void ExtractFromHeader_WithValidCorrelationId_ReturnsId()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var request = new DefaultHttpRequest(new DefaultHttpContext())
        {
            Headers = new HeaderDictionary
            {
                { CorrelationIdExtensions.CorrelationIdHeader, correlationId.ToString() }
            }
        };

        // Act
        var extracted = request.ExtractOrGenerateCorrelationId();

        // Assert
        Assert.Equal(correlationId, extracted);
    }

    // ========================================================================
    // Test: Generate if Missing
    // ========================================================================
    [Fact]
    public void ExtractOrGenerate_WithoutHeader_GeneratesNew()
    {
        // Arrange
        var request = new DefaultHttpRequest(new DefaultHttpContext())
        {
            Headers = new HeaderDictionary()
        };

        // Act
        var extracted = request.ExtractOrGenerateCorrelationId();

        // Assert
        Assert.NotEqual(Guid.Empty, extracted);
    }

    // ========================================================================
    // Test: Generate if Invalid Format
    // ========================================================================
    [Fact]
    public void ExtractOrGenerate_WithInvalidFormat_GeneratesNew()
    {
        // Arrange
        var request = new DefaultHttpRequest(new DefaultHttpContext())
        {
            Headers = new HeaderDictionary
            {
                { CorrelationIdExtensions.CorrelationIdHeader, "not-a-guid" }
            }
        };

        // Act
        var extracted = request.ExtractOrGenerateCorrelationId();

        // Assert
        Assert.NotEqual(Guid.Empty, extracted);
    }

    // ========================================================================
    // Test: Add to Response Header
    // ========================================================================
    [Fact]
    public void AddCorrelationIdHeader_AddsToResponse()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var response = new DefaultHttpResponse(new DefaultHttpContext());

        // Act
        response.AddCorrelationIdHeader(correlationId);

        // Assert
        var header = response.Headers[CorrelationIdExtensions.CorrelationIdHeader];
        Assert.Equal(correlationId.ToString(), header.ToString());
    }

    // ========================================================================
    // Test: Context Storage and Retrieval
    // ========================================================================
    [Fact]
    public void CorrelationIdContext_Set_And_Get()
    {
        // Arrange
        var correlationId = Guid.NewGuid();

        // Act
        CorrelationIdContext.Set(correlationId);
        var retrieved = CorrelationIdContext.Current;

        // Assert
        Assert.Equal(correlationId, retrieved);

        // Cleanup
        CorrelationIdContext.Clear();
    }

    // ========================================================================
    // Test: Context Clear
    // ========================================================================
    [Fact]
    public void CorrelationIdContext_Clear_RemovesId()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        CorrelationIdContext.Set(correlationId);

        // Act
        CorrelationIdContext.Clear();
        var retrieved = CorrelationIdContext.Current;

        // Assert
        Assert.Equal(Guid.Empty, retrieved);
    }

    // ========================================================================
    // Test: Context Scoped Usage
    // ========================================================================
    [Fact]
    public void CorrelationIdContext_Use_ScopedStorage()
    {
        // Arrange
        var outerCorrelationId = Guid.NewGuid();
        var innerCorrelationId = Guid.NewGuid();

        // Act - Set outer
        CorrelationIdContext.Set(outerCorrelationId);
        Assert.Equal(outerCorrelationId, CorrelationIdContext.Current);

        // Act - Use inner scope
        using (CorrelationIdContext.Use(innerCorrelationId))
        {
            Assert.Equal(innerCorrelationId, CorrelationIdContext.Current);
        }

        // Assert - Back to outer
        Assert.Equal(outerCorrelationId, CorrelationIdContext.Current);

        // Cleanup
        CorrelationIdContext.Clear();
    }

    // ========================================================================
    // Test: Empty GUID Handling
    // ========================================================================
    [Fact]
    public void ExtractOrGenerate_WithEmptyGUID_GeneratesNew()
    {
        // Arrange
        var request = new DefaultHttpRequest(new DefaultHttpContext())
        {
            Headers = new HeaderDictionary
            {
                { CorrelationIdExtensions.CorrelationIdHeader, Guid.Empty.ToString() }
            }
        };

        // Act
        var extracted = request.ExtractOrGenerateCorrelationId();

        // Assert
        Assert.NotEqual(Guid.Empty, extracted);
    }

    // ========================================================================
    // Test: Multiple Extractions Are Consistent
    // ========================================================================
    [Fact]
    public void ExtractOrGenerate_Idempotent_ReturnsSameValue()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var request = new DefaultHttpRequest(new DefaultHttpContext())
        {
            Headers = new HeaderDictionary
            {
                { CorrelationIdExtensions.CorrelationIdHeader, correlationId.ToString() }
            }
        };

        // Act
        var extracted1 = request.ExtractOrGenerateCorrelationId();
        var extracted2 = request.ExtractOrGenerateCorrelationId();

        // Assert
        Assert.Equal(extracted1, extracted2);
        Assert.Equal(correlationId, extracted1);
    }
}
