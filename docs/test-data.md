# Test-data builders

The FactoryBot / object-mother analog for this repo (rails-parity plan **P2.2**).
Tests should not hand-assemble entities field-by-field — that couples every test
to the full shape of the model and buries the *one* field the test is actually
about. Instead, each entity gets a **fluent builder** that produces a valid
instance by default.

## Where they live

```
tests/DotnetAgenticStarterkit.Tests/TestData/
  ListingBuilder.cs        # one builder per entity
  ListingBuilderTests.cs   # guards the builder itself
```

Namespace `DotnetAgenticStarterkit.Tests.TestData`, mirroring the folder (same rule as the
rest of the test project).

## The shape

`ListingBuilder` is the worked example. Every builder follows the same contract:

| Member | Purpose |
|---|---|
| `new XBuilder()` | valid-by-default; no arguments needed |
| `new XBuilder(seed: 123)` | distinct data, still repeatable |
| `.WithFoo(value)` | pin one field; returns `this` for chaining |
| `.With(x => x.Foo = value)` | escape hatch for fields without a `With*` |
| `.Build()` | one entity |
| `.BuildMany(n)` | `IReadOnlyList<X>` of `n`, from one continuous sequence, every override applied to each |
| `XBuilder.Valid()` | `static` shorthand for `new XBuilder().Build()` |

The entity's key (`Id`) is left at its default — EF Core assigns it on insert.

## Determinism

`CLAUDE.md` requires deterministic test data. Builders get realistic values from
[Bogus](https://github.com/bchavez/Bogus) (`Bogus` package, central-managed
version), but the `Faker<T>` is pinned with **`.UseSeed(...)`**:

- `new ListingBuilder()` uses `ListingBuilder.DefaultSeed` — the same values on
  every run and every machine.
- `new ListingBuilder(seed: n)` — a different dataset, equally repeatable. Use
  this when a test needs two builders that must not collide, or several
  independent rows.
- Nothing in a builder may read the wall clock, the network, the environment, or
  the filesystem. Date fields are generated relative to a fixed reference date
  held in the builder, not `DateTime.Today`.

## Usage

```csharp
// Just need a valid row — don't care about the values:
var listing = ListingBuilder.Valid();

// Pin the fields under test, let the rest be realistic filler:
var sold = new ListingBuilder()
    .WithCity("Bristol")
    .WithPrice(465_000m)
    .WithStatus(ListingStatus.Sold)
    .Build();

// A page of rows for a grid / paging test:
IReadOnlyList<Listing> page = new ListingBuilder().BuildMany(25);
```

## Adding a builder for a new entity

1. `tests/DotnetAgenticStarterkit.Tests/TestData/<Entity>Builder.cs`, `sealed`, same member
   contract as the table above.
2. Seed the `Faker<T>` in the constructor; every `RuleFor` must stay inside the
   entity's data-annotation constraints so `Build()` is valid by default.
3. Add a `<Entity>BuilderTests.cs` covering: valid-by-default, an override wins,
   same-seed-same-data, `BuildMany` count + override propagation.
4. No `With*` for a field until a test needs it — `.With(...)` covers the rest.
