# E2ETests

End-to-End (E2E) tests for the Cbot to MQL5 Translation system.

## Overview

This test suite simulates the complete flow of trade synchronization:
- Ctrader cBot → Bridge Server → MT5 EA

## Running Tests

```bash
# Run all E2E tests
dotnet test

# Run with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run specific test
dotnet test --filter "FullyQualifiedName~E2E_PositionOpened"
```

## Test Cases

- **E2E_PositionOpened_CompleteFlow_ShouldSucceed**: Tests complete position open flow
- **E2E_PositionModified_CompleteFlow_ShouldSucceed**: Tests position modification flow
- **E2E_PositionClosed_CompleteFlow_ShouldSucceed**: Tests position close flow
- **E2E_MultipleOrders_ProcessedInOrder_ShouldSucceed**: Tests FIFO order processing
- **E2E_TicketMapping_CompleteFlow_ShouldSucceed**: Tests ticket mapping functionality
- **E2E_ErrorRecovery_OrderRetry_ShouldSucceed**: Tests error recovery and retry mechanism
- **E2E_HealthCheck_ShouldAlwaysBeAccessible**: Tests health check endpoint

## Documentation

For more information, see [E2E Testing Guide](../docs/E2E_TESTING.md).
