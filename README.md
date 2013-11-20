CacheSleeve - Distributed In-Memory Caching for .NET
===========


Overview
--------
CacheSleeve lets you easily add distributed in-memory caching to your ASP.NET projects.

Using CacheSleeves HybridCacher you can simply add items to the cache and they will automatically be synced across all servers in your farm using a combination of Redis and in-memory caching.

Uses Marc Gravell's [BookSleeve](https://code.google.com/p/booksleeve/) for interacting with Redis.

Features
--------
### Efficiency

CacheSleeve primarily increases efficiency over non-distributed caching (i.e. each server has it's own isolated cache) by preventing each web server from having to fetch it's own cache items from the database.
With CacheSleeve only one database call is made to populate the distributed cache. Once the item is in the distributed cache all servers will use that item until it is invalidated.

The second way that CacheSleeve increases efficiency is by storing cache items in each web servers in-memory cache upon first request. This means once a web server has requested a cache item
it will use its local in-memory cache until the item is invalidated in the distributed cache. 


### Automatic Invalidation

> There are only two hard things in Computer Science: cache invalidation and naming things.
>             -- Phil Karlton

All cache invalidation is synced across all connected servers. Remove a cache item on one server and it will automatically be invalidated on all servers.

This invalidation also works for keys which are invalidated due to an expiration date/time and for parent/child relationships.


### Cache Dependency

Set parent/child relationships for cache items. Removing a parent item will invalidate its children across all connected servers.


### Cache Expiration

Set a time span that cache items should live for. When the item expires it will be invalidated across all connected servers.


Setup
-----
### Initialize the CacheManager

Before you can start using the distributed cache you will need to initialize the CacheSleeve CacheManager with connection details for your Redis server.
This should only be done once per server connecting to the distributed cache, usually in Application_Start.

If you're running redis on localhost with default settings the following is all you need to get the cache manager running:

```
CacheSleeve.CacheManager.Init("localhost");
```

Once the cache manager has been initialized You can use the HybridCacher to interact with the distributed cache.

#### Server A
```
var cacher = new CacheSleeve.HybridCacher();
cacher.Set("key", "value");
```

This item is now in the distributed cache and will be synced/invalidated across all connected servers.

#### Server B
```
var cacher = new CacheSleeve.HybridCacher();
var item = cacher.Get<string>("key");
```

Here _item_ is equal to "value" and the cache item has also been stored in Server B's in-memory cache for subsequent requests.

#### Server A (again)
```
cacher.Remove("key");
```

This invalidates the item with the key "key" in Server A's in-memory cache, the distributed cache, Server B's in-memory cache, and any other connected servers in-memory cache.

#### Server B (again)
```
var item = cacher.Get<string>("key");
```

Here _item_ is now null because it was invalidated on Server A.


Examples
--------
Examples of more advanced usage coming soon


Dependency Injection
--------------------
You can also use the HybridCacher without hard coding new instances into your code. HybridCacher implements the CacheSleeve.ICacher interface which you can use to inject the dependency.

Example dependency setup (StructureMap):
```
  For<ICacher>().Use(new HybridCacher());
```

You can then use constructor injection (or property injection) to inject the dependency.