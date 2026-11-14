using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgenticWorkflowPoC.Plugins.Financial;

namespace AgenticWorkflowPoC.Api.Development
{
    /// <summary>
    /// Development/test ledger with the same semantics as the SQL implementation:
    /// a single lock plays the role of the SERIALIZABLE transaction + account UPDLOCK.
    /// Not durable across process restarts.
    /// </summary>
    public sealed class InMemoryLedger : ILedgerReader, ITransferExecutor
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<(string TenantId, string AccountId), Account> _accounts = [];
        private readonly Dictionary<(string TenantId, Guid OperationId), TransferRecord> _transfers = [];
        private readonly HashSet<(string TenantId, string Iban)> _blockedBeneficiaries = [];

        public int ExecutedCount
        {
            get
            {
                lock (_gate)
                {
                    return _transfers.Count;
                }
            }
        }

        public InMemoryLedger SeedAccount(string tenantId, string accountId, string currency, decimal balance)
        {
            lock (_gate)
            {
                _accounts[(tenantId, accountId)] = new Account(currency, balance);
            }

            return this;
        }

        public void Freeze(string tenantId, string accountId)
        {
            lock (_gate)
            {
                _accounts[(tenantId, accountId)].IsFrozen = true;
            }
        }

        public void BlockBeneficiary(string tenantId, string iban)
        {
            lock (_gate)
            {
                _blockedBeneficiaries.Add((tenantId, iban));
            }
        }

        public decimal GetBalance(string tenantId, string accountId)
        {
            lock (_gate)
            {
                return _accounts[(tenantId, accountId)].Balance;
            }
        }

        public Task<LedgerSnapshot> GetSnapshotAsync(LedgerQuery query, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _accounts.TryGetValue((query.TenantId, query.SourceAccountId), out var account);
                return Task.FromResult(new LedgerSnapshot(
                    account is not null,
                    account?.Currency,
                    account?.Balance ?? 0m,
                    account?.IsFrozen ?? false,
                    DebitedToday(query.TenantId, query.SourceAccountId, query.AsOfUtc),
                    _blockedBeneficiaries.Contains((query.TenantId, query.DestinationAccount)),
                    _transfers.ContainsKey((query.TenantId, query.OperationId))));
            }
        }

        public Task<TransferExecutionStatus> ExecuteAsync(TransferExecutionRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var command = request.Command;

            lock (_gate)
            {
                if (_transfers.TryGetValue((command.TenantId, request.OperationId), out var existing))
                {
                    if (existing.SourceAccountId != command.SourceAccountId
                        || existing.DestinationAccount != command.DestinationAccount
                        || existing.Amount != command.Amount
                        || existing.Currency != command.Currency)
                    {
                        return Task.FromResult(TransferExecutionStatus.IdempotencyConflict);
                    }

                    return Task.FromResult(TransferExecutionStatus.AlreadyExecuted);
                }

                _accounts.TryGetValue((command.TenantId, command.SourceAccountId), out var account);
                var status = account switch
                {
                    null => TransferExecutionStatus.AccountNotFound,
                    { IsFrozen: true } => TransferExecutionStatus.AccountFrozen,
                    _ when account.Currency != command.Currency => TransferExecutionStatus.CurrencyMismatch,
                    _ when account.Balance < command.Amount => TransferExecutionStatus.InsufficientFunds,
                    _ when DebitedToday(command.TenantId, command.SourceAccountId, request.AsOfUtc) + command.Amount > request.DailyLimit
                        => TransferExecutionStatus.DailyLimitExceeded,
                    _ => TransferExecutionStatus.Executed
                };

                if (status == TransferExecutionStatus.Executed)
                {
                    account!.Balance -= command.Amount;
                    _transfers[(command.TenantId, request.OperationId)] = new TransferRecord(
                        command.SourceAccountId,
                        command.DestinationAccount,
                        command.Amount,
                        command.Currency,
                        request.AsOfUtc);
                }

                return Task.FromResult(status);
            }
        }

        private decimal DebitedToday(string tenantId, string accountId, DateTimeOffset asOf)
        {
            var dayStart = new DateTimeOffset(asOf.UtcDateTime.Date, TimeSpan.Zero);
            var dayEnd = dayStart.AddDays(1);
            return _transfers
                .Where(t => t.Key.TenantId == tenantId
                    && t.Value.SourceAccountId == accountId
                    && t.Value.ExecutedAt >= dayStart
                    && t.Value.ExecutedAt < dayEnd)
                .Sum(t => t.Value.Amount);
        }

        private sealed class Account(string currency, decimal balance)
        {
            public string Currency { get; } = currency;
            public decimal Balance { get; set; } = balance;
            public bool IsFrozen { get; set; }
        }

        private sealed record TransferRecord(
            string SourceAccountId,
            string DestinationAccount,
            decimal Amount,
            string Currency,
            DateTimeOffset ExecutedAt);
    }
}
