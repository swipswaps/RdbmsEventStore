﻿using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;
using Nito.AsyncEx;

namespace RdbmsEventStore.EntityFramework
{
    public class EntityFrameworkEventStore<TId, TContext, TEvent> : IEventStore<TId, TEvent>
        where TId : IEquatable<TId>
        where TContext : DbContext, IEventDbContext<TEvent>
        where TEvent : Event<TId>, IEvent<TId>, new()
    {
        private readonly AsyncLock _mutex = new AsyncLock();
        private readonly TContext context;
        private readonly IEventFactory<TId, TEvent> _eventFactory;

        public EntityFrameworkEventStore(TContext context, IEventFactory<TId, TEvent> eventFactory)
        {
            this.context = context;
            _eventFactory = eventFactory;
        }

        public Task<IEnumerable<TEvent>> Events(TId streamId) => Events(streamId, query => query);

        public async Task<IEnumerable<TEvent>> Events(TId streamId, Func<IQueryable<TEvent>, IQueryable<TEvent>> query)
            => await context.Events
                .Where(e => e.StreamId.Equals(streamId))
                .Apply(query)
                .AsNoTracking()
                .ToListAsync();

        public async Task Commit<T>(TId streamId, params T[] payloads)
        {
            using (await _mutex.LockAsync())
            {
                var highestVersionNumber = await context.Events
                    .Where(e => e.StreamId.Equals(streamId))
                    .Select(e => e.Version)
                    .DefaultIfEmpty(0L)
                    .MaxAsync();
                var events = payloads
                    .Zip(VersionsFollowing(highestVersionNumber, payloads.Length),
                    (payload, version) => _eventFactory.Create(streamId, version, payload));
                context.Events.AddRange(events);
                await context.SaveChangesAsync();
            }
        }

        private static IEnumerable<long> VersionsFollowing(long versionNumber, long count)
        {
            for (var i = versionNumber + 1; i <= versionNumber + count; i++)
            {
                yield return i;
            }
        }
    }
}
