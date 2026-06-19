using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GitHub.Runner.Common.Tests
{
    public sealed class RetryHelperL0
    {
        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public async Task ExecuteAsync_SucceedsOnFirstAttempt()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var trace = hc.GetTrace();
                var strategy = new RetryStrategy
                {
                    MaxAttempts = 3,
                    GetBackoff = (_, _, _) => TimeSpan.Zero,
                };
                var helper = new RetryHelper(trace, strategy);

                var result = await helper.ExecuteAsync(() => Task.FromResult(42));

                Assert.Equal(42, result);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public async Task ExecuteAsync_RetriesOnTransientFailure()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var trace = hc.GetTrace();
                var attempts = 0;
                var strategy = new RetryStrategy
                {
                    MaxAttempts = 3,
                    GetBackoff = (_, _, _) => TimeSpan.Zero,
                };
                var helper = new RetryHelper(trace, strategy);

                var result = await helper.ExecuteAsync<int>(() =>
                {
                    attempts++;
                    if (attempts < 3)
                    {
                        throw new InvalidOperationException("transient");
                    }
                    return Task.FromResult(attempts);
                });

                Assert.Equal(3, result);
                Assert.Equal(3, attempts);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public async Task ExecuteAsync_ThrowsAfterMaxAttempts()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var trace = hc.GetTrace();
                var strategy = new RetryStrategy
                {
                    MaxAttempts = 3,
                    GetBackoff = (_, _, _) => TimeSpan.Zero,
                };
                var helper = new RetryHelper(trace, strategy);

                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    helper.ExecuteAsync<int>(() => throw new InvalidOperationException("always fails")));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public async Task ExecuteAsync_InvokesOnRetryCallback()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var trace = hc.GetTrace();
                var onRetryCalls = 0;
                var strategy = new RetryStrategy
                {
                    MaxAttempts = 3,
                    GetBackoff = (_, _, _) => TimeSpan.Zero,
                    OnRetry = (_, _, _) => onRetryCalls++,
                };
                var helper = new RetryHelper(trace, strategy);
                var callCount = 0;

                await helper.ExecuteAsync<int>(() =>
                {
                    callCount++;
                    if (callCount < 3)
                    {
                        throw new InvalidOperationException("transient");
                    }
                    return Task.FromResult(0);
                });

                Assert.Equal(2, onRetryCalls);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public async Task ExecuteAsync_StopsRetryingWhenShouldRetryReturnsFalse()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var trace = hc.GetTrace();
                var strategy = new RetryStrategy
                {
                    MaxAttempts = 5,
                    GetBackoff = (_, _, _) => TimeSpan.Zero,
                    ShouldRetry = ex => ex is InvalidOperationException,
                };
                var helper = new RetryHelper(trace, strategy);

                await Assert.ThrowsAsync<ArgumentException>(() =>
                    helper.ExecuteAsync<int>(() => throw new ArgumentException("non-retryable")));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public async Task ExecuteAsync_ThrowsOperationCanceledWhenTokenCancelled()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var trace = hc.GetTrace();
                using var cts = new CancellationTokenSource();
                var strategy = new RetryStrategy
                {
                    MaxAttempts = 5,
                    GetBackoff = (_, _, _) => TimeSpan.Zero,
                };
                var helper = new RetryHelper(trace, strategy);

                cts.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    helper.ExecuteAsync(() => Task.FromResult(0), cts.Token));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void Constructor_ThrowsWhenMaxAttemptsIsZero()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var trace = hc.GetTrace();
                var strategy = new RetryStrategy
                {
                    MaxAttempts = 0,
                    GetBackoff = (_, _, _) => TimeSpan.Zero,
                };

                Assert.Throws<ArgumentOutOfRangeException>(() => new RetryHelper(trace, strategy));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public void Constructor_ThrowsWhenGetBackoffIsNull()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var trace = hc.GetTrace();
                var strategy = new RetryStrategy
                {
                    MaxAttempts = 3,
                    GetBackoff = null,
                };

                Assert.Throws<ArgumentNullException>(() => new RetryHelper(trace, strategy));
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public async Task ExecuteAsync_InvokesOnSuccessCallback()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var trace = hc.GetTrace();
                RetryExecutionContext capturedContext = null;
                TimeSpan capturedDuration = TimeSpan.Zero;
                var strategy = new RetryStrategy
                {
                    MaxAttempts = 3,
                    GetBackoff = (_, _, _) => TimeSpan.Zero,
                    OnSuccess = (context, duration) =>
                    {
                        capturedContext = context;
                        capturedDuration = duration;
                    },
                };

                var helper = new RetryHelper(trace, strategy);

                var result = await helper.ExecuteAsync("test-operation", () => Task.FromResult(7));

                Assert.Equal(7, result);
                Assert.NotNull(capturedContext);
                Assert.Equal("test-operation", capturedContext.OperationName);
                Assert.Equal(1, capturedContext.AttemptNumber);
                Assert.True(capturedDuration >= TimeSpan.Zero);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public async Task ExecuteAsync_InvokesOnFailureForNonRetryableException()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var trace = hc.GetTrace();
                RetryExecutionContext capturedContext = null;
                Exception capturedException = null;
                TimeSpan capturedDuration = TimeSpan.Zero;
                var strategy = new RetryStrategy
                {
                    MaxAttempts = 5,
                    GetBackoff = (_, _, _) => TimeSpan.Zero,
                    ShouldRetry = ex => ex is InvalidOperationException,
                    OnFailure = (context, ex, duration) =>
                    {
                        capturedContext = context;
                        capturedException = ex;
                        capturedDuration = duration;
                    },
                };

                var helper = new RetryHelper(trace, strategy);

                await Assert.ThrowsAsync<ArgumentException>(() =>
                    helper.ExecuteAsync<int>("non-retryable-op", () => throw new ArgumentException("fail")));

                Assert.NotNull(capturedContext);
                Assert.Equal("non-retryable-op", capturedContext.OperationName);
                Assert.Equal(1, capturedContext.AttemptNumber);
                Assert.IsType<ArgumentException>(capturedException);
                Assert.True(capturedDuration >= TimeSpan.Zero);
            }
        }

        [Fact]
        [Trait("Level", "L0")]
        [Trait("Category", "Common")]
        public async Task ExecuteAsync_PassesContextToOperationCallback()
        {
            using (TestHostContext hc = CreateTestContext())
            {
                var trace = hc.GetTrace();
                var seenAttempts = new List<int>();
                var strategy = new RetryStrategy
                {
                    MaxAttempts = 3,
                    GetBackoff = (_, _, _) => TimeSpan.Zero,
                };

                var helper = new RetryHelper(trace, strategy);
                var result = await helper.ExecuteAsync<int>("context-op", context =>
                {
                    seenAttempts.Add(context.AttemptNumber);
                    if (context.AttemptNumber < context.MaxAttempts)
                    {
                        throw new InvalidOperationException("transient");
                    }

                    return Task.FromResult(context.AttemptNumber);
                });

                Assert.Equal(3, result);
                Assert.Equal(new[] { 1, 2, 3 }, seenAttempts);
            }
        }

        private TestHostContext CreateTestContext([CallerMemberName] string testName = "")
        {
            return new TestHostContext(this, testName);
        }
    }
}
