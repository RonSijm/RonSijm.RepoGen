using Microsoft.EntityFrameworkCore;
using RonSijm.RepoGen.ConfigurationExamples.Designers;
using RonSijm.RepoGen.ConfigurationExamples.Models.Basic;
using RonSijm.RepoGen.ConfigurationExamples.Models.BatchWrites;
using RonSijm.RepoGen.ConfigurationExamples.Models.Catalog;
using RonSijm.RepoGen.ConfigurationExamples.Models.Collections;
using RonSijm.RepoGen.ConfigurationExamples.Models.CompositeCollections;
using RonSijm.RepoGen.ConfigurationExamples.Models.ContractNotes;
using RonSijm.RepoGen.ConfigurationExamples.Models.FixtureTickets;
using RonSijm.RepoGen.ConfigurationExamples.Models.Inventory;
using RonSijm.RepoGen.ConfigurationExamples.Models.Ledger;
using RonSijm.RepoGen.ConfigurationExamples.Models.Library;
using RonSijm.RepoGen.ConfigurationExamples.Models.Membership;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryNotebook;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryCatalog;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryCoverage;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryPartitions;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryNavigation;
using RonSijm.RepoGen.ConfigurationExamples.Models.ProxyShelves;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemorySharedWrites;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryBatches;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryGeneratedValues;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryProxyWrites;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryKeyIdentity;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryImmediateWrites;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAtomicWrites;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryCollections;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemorySavedResults;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryBatchResults;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryCreateBatches;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryManyToMany;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryOptionalLinks;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryLinks;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryTranslations;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryWriteTranslations;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryScalarEquality;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemorySharedLinks;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryAdditionalReferences;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryOptionalReferences;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryNullTargets;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryRelationshipComparers;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryProjectionHelpers;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryModelFilters;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryModelWrites;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryLoadingPolicies;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryModelDefaults;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryFilteredNavigation;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemorySavedFilters;
using RonSijm.RepoGen.ConfigurationExamples.Models.MemoryNativeFilters;
using RonSijm.RepoGen.ConfigurationExamples.Models.MultiContext;
using RonSijm.RepoGen.ConfigurationExamples.Models.OrderReports;
using RonSijm.RepoGen.ConfigurationExamples.Models.ProxyDirectory;
using RonSijm.RepoGen.ConfigurationExamples.Models.Reporting;
using RonSijm.RepoGen.ConfigurationExamples.Models.ScriptedFeed;
using RonSijm.RepoGen.ConfigurationExamples.Models.ScriptedNotes;
using RonSijm.RepoGen.ConfigurationExamples.Models.TaskDesk;
using RonSijm.RepoGen.ConfigurationExamples.Models.Visibility;
using RonSijm.RepoGen.ConfigurationExamples.Models.Workboard;
using RonSijm.RepoGen.ConfigurationExamples.Models.WriteSafety;
using RonSijm.RepoGen.Design;
using RonSijm.RepoGen.Tool;
using RonSijm.RepoGen.Validation;

namespace RonSijm.RepoGen.ConfigurationExamples.GenerationRunner;

internal sealed class Program
{
    private static int Main(string[] args)
    {
        // An independent design-time executable: it never references generated persistence projects.
        if (args.Length < 1)
        {
            Usage();
            return 2;
        }

        var root = Path.GetFullPath(args[0]);
        var check = false;
        string? referenceFile = null;
        for (var index = 1; index < args.Length; index++)
        {
            if (args[index] == "--check" && !check)
            {
                check = true;
            }
            else if (args[index] == "--compile-references" && referenceFile is null && index + 1 < args.Length)
            {
                referenceFile = args[++index];
            }
            else
            {
                Usage();
                return 2;
            }
        }

        RepositoryCompilationOptions? compilation = null;
        if (referenceFile is not null)
        {
            if (!File.Exists(referenceFile))
            {
                Console.Error.WriteLine("RGV1001: Missing target compilation reference list: " + referenceFile);
                return 5;
            }

            var references = File.ReadAllLines(referenceFile);
            var contractReferences = references.Where(path =>
            {
                var name = Path.GetFileNameWithoutExtension(path);
                return name is "mscorlib" or "netstandard" or "System" or "Microsoft.CSharp" || name.StartsWith("System.", StringComparison.Ordinal) ||
                       name is "Contracts" or "RonSijm.RepoGen.Abstractions";
            });
            var testingReferences = contractReferences.Concat(references.Where(path => Path.GetFileNameWithoutExtension(path) is
                "RonSijm.RepoGen.Testing" or "Microsoft.Extensions.DependencyInjection.Abstractions"));
            compilation = new(references, contractReferences, languageVersion: "12", warningsAsErrors: true, testDoubleReferences: testingReferences);
        }

        var stale = false;
        var incompatibleApi = false;
        var invalidCompilation = false;
        var publish = new List<Action>();
        using var notes = new NotesContext(Options<NotesContext>());
        using var catalog = new CatalogContext(Options<CatalogContext>());
        using var reporting = new ReportingContext(Options<ReportingContext>());
        using var library = new LibraryContext(Options<LibraryContext>());
        using var ledger = new LedgerContext(Options<LedgerContext>());
        using var inventory = new InventoryContext(Options<InventoryContext>());
        using var collections = new CollectionsContext(Options<CollectionsContext>());
        using var writeSafety = new WriteSafetyContext(Options<WriteSafetyContext>());
        using var batchWrites = new BatchWritesContext(Options<BatchWritesContext>());
        using var workboard = new WorkboardContext(Options<WorkboardContext>());
        using var taskDesk = new TaskDeskContext(Options<TaskDeskContext>());
        using var directory = new LookupDirectoryContext(Options<LookupDirectoryContext>());
        using var scriptedNotes = new ScriptedNotesContext(Options<ScriptedNotesContext>());
        using var scriptedFeed = new ScriptedFeedContext(Options<ScriptedFeedContext>());
        using var memoryNotebook = new MemoryNotebookContext(Options<MemoryNotebookContext>());
        using var memoryPartitions = new PartitionDirectoryContext(Options<PartitionDirectoryContext>());
        using var memoryCatalog = new MemoryCatalogContext(Options<MemoryCatalogContext>());
        using var memoryCoverage = new MemoryCoverageContext(Options<MemoryCoverageContext>());
        using var contractNotes = new ContractNotesContext(Options<ContractNotesContext>());
        using var fixtureTickets = new FixtureTicketsContext(Options<FixtureTicketsContext>());
        using var orderReports = new OrderReportsContext(Options<OrderReportsContext>());
        using var proxyDirectory = new ProxyDirectoryContext(Options<ProxyDirectoryContext>());
        using var navigationCatalog = new NavigationCatalogContext(Options<NavigationCatalogContext>());
        using var proxyShelves = new ProxyShelfContext(Options<ProxyShelfContext>());
        using var taggedArticles = new TaggedArticleContext(Options<TaggedArticleContext>());
        using var linkedNotes = new LinkedNoteContext(Options<LinkedNoteContext>());
        using var boardLabels = new BoardLabelsContext(Options<BoardLabelsContext>());
        using var draftWorkspace = new DraftWorkspaceContext(Options<DraftWorkspaceContext>());
        using var batchWorkspace = new BatchWorkspaceContext(Options<BatchWorkspaceContext>());
        using var generatedNotebook = new GeneratedNotebookContext(Options<GeneratedNotebookContext>());
        using var proxyNotebook = new ProxyNotebookContext(Options<ProxyNotebookContext>());
        using var keyDirectory = new KeyDirectoryContext(Options<KeyDirectoryContext>());
        using var dispatchQueue = new DispatchQueueContext(Options<DispatchQueueContext>());
        using var reservationDirectory = new ReservationDirectoryContext(Options<ReservationDirectoryContext>());
        using var checklistWorkspace = new ChecklistWorkspaceContext(Options<ChecklistWorkspaceContext>());
        using var translationCatalog = new TranslationCatalogContext(Options<TranslationCatalogContext>());
        using var savedNotebook = new SavedNotebookContext(Options<SavedNotebookContext>());
        using var batchNotebook = new BatchNotebookContext(Options<BatchNotebookContext>());
        using var intakeCatalog = new IntakeCatalogContext(Options<IntakeCatalogContext>());
        using var patternCatalog = new PatternCatalogContext(Options<PatternCatalogContext>());
        using var patternNotes = new PatternNotesContext(Options<PatternNotesContext>());
        using var comparisonNotes = new ComparisonNotesContext(Options<ComparisonNotesContext>());
        using var scopedResources = new ScopedResourcesContext(Options<ScopedResourcesContext>());
        using var referenceCatalog = new ReferenceCatalogContext(Options<ReferenceCatalogContext>());
        using var optionalAssignments = new OptionalAssignmentsContext(Options<OptionalAssignmentsContext>());
        using var nullTargetNotebook = new NullTargetNotebookContext(Options<NullTargetNotebookContext>());
        using var comparedDirectory = new ComparedDirectoryContext(Options<ComparedDirectoryContext>());
        using var projectionMemos = new ProjectionMemosContext(Options<ProjectionMemosContext>());
        using var filteredNotebook = new FilteredNotebookContext(Options<FilteredNotebookContext>());
        using var filteredDrafts = new FilteredDraftsContext(Options<FilteredDraftsContext>());
        using var autoLoadedCatalog = new AutoLoadedCatalogContext(Options<AutoLoadedCatalogContext>());
        using var defaultSettings = new DefaultSettingsContext(Options<DefaultSettingsContext>());
        using var filteredShelves = new FilteredShelfContext(Options<FilteredShelfContext>());
        using var savedShelves = new SavedShelfContext(Options<SavedShelfContext>());
        using var nativeNotebook = new NativeNotebookContext(Options<NativeNotebookContext>());
        // Explicit designer -> context -> output mapping; no designer is discovered by a consumer Program.cs.
        Generate(draftWorkspace, new DraftWorkspaceRepositoryDesigner().CreateModel(), "MemorySharedWrites", contracts: "Contracts/Generated/DraftWorkspaceRepositories.g.cs",
            testDoubles: "MemorySharedWrites/Generated/TestDoubles.g.cs", persistence: "MemorySharedWrites/Persistence/Repositories.g.cs");
        Generate(batchWorkspace, new BatchWorkspaceRepositoryDesigner().CreateModel(), "MemoryBatches", contracts: "Contracts/Generated/BatchWorkspaceRepositories.g.cs",
            testDoubles: "MemoryBatches/Generated/TestDoubles.g.cs", persistence: "MemoryBatches/Persistence/Repositories.g.cs");
        Generate(generatedNotebook, new GeneratedNotebookRepositoryDesigner().CreateModel(), "MemoryGeneratedValues", contracts: "Contracts/Generated/GeneratedNotebookRepositories.g.cs",
            testDoubles: "MemoryGeneratedValues/Generated/TestDoubles.g.cs", persistence: "MemoryGeneratedValues/Persistence/Repositories.g.cs");
        Generate(proxyNotebook, new ProxyNotebookRepositoryDesigner().CreateModel(), "MemoryProxyWrites", contracts: "Contracts/Generated/ProxyNotebookRepositories.g.cs",
            testDoubles: "MemoryProxyWrites/Generated/TestDoubles.g.cs", persistence: "MemoryProxyWrites/Persistence/Repositories.g.cs");
        Generate(keyDirectory, new KeyDirectoryRepositoryDesigner().CreateModel(), "MemoryKeyIdentity", contracts: "Contracts/Generated/KeyDirectoryRepositories.g.cs",
            testDoubles: "MemoryKeyIdentity/Generated/TestDoubles.g.cs", persistence: "MemoryKeyIdentity/Persistence/Repositories.g.cs");
        Generate(dispatchQueue, new DispatchQueueRepositoryDesigner().CreateModel(), "MemoryImmediateWrites", contracts: "Contracts/Generated/DispatchQueueRepositories.g.cs",
            testDoubles: "MemoryImmediateWrites/Generated/TestDoubles.g.cs", persistence: "MemoryImmediateWrites/Persistence/Repositories.g.cs");
        Generate(reservationDirectory, new ReservationDirectoryRepositoryDesigner().CreateModel(), "MemoryAtomicWrites", contracts: "Contracts/Generated/ReservationDirectoryRepositories.g.cs",
            testDoubles: "MemoryAtomicWrites/Generated/TestDoubles.g.cs", persistence: "MemoryAtomicWrites/Persistence/Repositories.g.cs");
        Generate(checklistWorkspace, new ChecklistWorkspaceRepositoryDesigner().CreateModel(), "MemoryCollections", contracts: "Contracts/Generated/ChecklistWorkspaceRepositories.g.cs",
            testDoubles: "MemoryCollections/Generated/TestDoubles.g.cs", persistence: "MemoryCollections/Persistence/Repositories.g.cs");
        Generate(translationCatalog, new TranslationCatalogRepositoryDesigner().CreateModel(), "CompositeCollections", contracts: "Contracts/Generated/TranslationCatalogRepositories.g.cs",
            testDoubles: "CompositeCollections/Generated/TestDoubles.g.cs", persistence: "CompositeCollections/Persistence/Repositories.g.cs");
        Generate(savedNotebook, new SavedNotebookRepositoryDesigner().CreateModel(), "MemorySavedResults", contracts: "Contracts/Generated/SavedNotebookRepositories.g.cs",
            testDoubles: "MemorySavedResults/Generated/TestDoubles.g.cs", persistence: "MemorySavedResults/Persistence/Repositories.g.cs");
        Generate(batchNotebook, new BatchNotebookRepositoryDesigner().CreateModel(), "MemoryBatchResults", contracts: "Contracts/Generated/BatchNotebookRepositories.g.cs",
            testDoubles: "MemoryBatchResults/Generated/TestDoubles.g.cs", persistence: "MemoryBatchResults/Persistence/Repositories.g.cs");
        Generate(intakeCatalog, new IntakeCatalogRepositoryDesigner().CreateModel(), "MemoryCreateBatches", contracts: "Contracts/Generated/IntakeCatalogRepositories.g.cs",
            testDoubles: "MemoryCreateBatches/Generated/TestDoubles.g.cs", persistence: "MemoryCreateBatches/Persistence/Repositories.g.cs");
        // Each example's README maps these calls and designer declarations to the exact generated files/APIs.
        Generate(proxyShelves, new ProxyShelfRepositoryDesigner().CreateModel(), "MemoryProxyNavigation", contracts: "Contracts/Generated/ProxyShelfRepositories.g.cs",
            testDoubles: "MemoryProxyNavigation/Generated/TestDoubles.g.cs", persistence: "MemoryProxyNavigation/Persistence/Repositories.g.cs");
        Generate(taggedArticles, new TaggedArticleRepositoryDesigner().CreateModel(), "MemoryManyToMany", contracts: "Contracts/Generated/TaggedArticleRepositories.g.cs",
            testDoubles: "MemoryManyToMany/Generated/TestDoubles.g.cs", persistence: "MemoryManyToMany/Persistence/Repositories.g.cs");
        Generate(linkedNotes, new LinkedNotesRepositoryDesigner().CreateModel(), "MemoryOptionalLinks", contracts: "Contracts/Generated/LinkedNoteRepositories.g.cs",
            testDoubles: "MemoryOptionalLinks/Generated/TestDoubles.g.cs", persistence: "MemoryOptionalLinks/Persistence/Repositories.g.cs");
        Generate(boardLabels, new BoardLabelsRepositoryDesigner().CreateModel(), "MemoryLinks", contracts: "Contracts/Generated/BoardLabelsRepositories.g.cs",
            testDoubles: "MemoryLinks/Generated/TestDoubles.g.cs", persistence: "MemoryLinks/Persistence/Repositories.g.cs");
        Generate(patternCatalog, new PatternCatalogRepositoryDesigner().CreateModel(), "MemoryTranslations", contracts: "Contracts/Generated/PatternCatalogRepositories.g.cs",
            testDoubles: "MemoryTranslations/Generated/TestDoubles.g.cs", persistence: "MemoryTranslations/Persistence/Repositories.g.cs");
        Generate(patternNotes, new PatternNotesRepositoryDesigner().CreateModel(), "MemoryWriteTranslations", contracts: "Contracts/Generated/PatternNotesRepositories.g.cs",
            testDoubles: "MemoryWriteTranslations/Generated/TestDoubles.g.cs", persistence: "MemoryWriteTranslations/Persistence/Repositories.g.cs");
        Generate(comparisonNotes, new ComparisonNotesRepositoryDesigner().CreateModel(), "MemoryScalarEquality", contracts: "Contracts/Generated/ComparisonNotesRepositories.g.cs",
            testDoubles: "MemoryScalarEquality/Generated/TestDoubles.g.cs", persistence: "MemoryScalarEquality/Persistence/Repositories.g.cs");
        Generate(scopedResources, new ScopedResourcesRepositoryDesigner().CreateModel(), "MemorySharedLinks", contracts: "Contracts/Generated/ScopedResourcesRepositories.g.cs",
            testDoubles: "MemorySharedLinks/Generated/TestDoubles.g.cs", persistence: "MemorySharedLinks/Persistence/Repositories.g.cs");
        Generate(referenceCatalog, new ReferenceCatalogRepositoryDesigner().CreateModel(), "MemoryAdditionalReferences", contracts: "Contracts/Generated/ReferenceCatalogRepositories.g.cs",
            testDoubles: "MemoryAdditionalReferences/Generated/TestDoubles.g.cs", persistence: "MemoryAdditionalReferences/Persistence/Repositories.g.cs");
        Generate(optionalAssignments, new OptionalAssignmentsRepositoryDesigner().CreateModel(), "MemoryOptionalReferences", contracts: "Contracts/Generated/OptionalAssignmentsRepositories.g.cs",
            testDoubles: "MemoryOptionalReferences/Generated/TestDoubles.g.cs", persistence: "MemoryOptionalReferences/Persistence/Repositories.g.cs");
        Generate(nullTargetNotebook, new NullTargetNotebookRepositoryDesigner().CreateModel(), "MemoryNullTargets", contracts: "Contracts/Generated/NullTargetNotebookRepositories.g.cs",
            testDoubles: "MemoryNullTargets/Generated/TestDoubles.g.cs", persistence: "MemoryNullTargets/Persistence/Repositories.g.cs");
        Generate(comparedDirectory, new ComparedDirectoryRepositoryDesigner().CreateModel(), "MemoryRelationshipComparers", contracts: "Contracts/Generated/ComparedDirectoryRepositories.g.cs",
            testDoubles: "MemoryRelationshipComparers/Generated/TestDoubles.g.cs", persistence: "MemoryRelationshipComparers/Persistence/Repositories.g.cs");
        Generate(projectionMemos, new ProjectionMemosRepositoryDesigner().CreateModel(), "MemoryProjectionHelpers", contracts: "Contracts/Generated/ProjectionMemosRepositories.g.cs",
            testDoubles: "MemoryProjectionHelpers/Generated/TestDoubles.g.cs", persistence: "MemoryProjectionHelpers/Persistence/Repositories.g.cs");
        Generate(filteredNotebook, new FilteredNotebookRepositoryDesigner().CreateModel(), "MemoryModelFilters", contracts: "Contracts/Generated/FilteredNotebookRepositories.g.cs",
            testDoubles: "MemoryModelFilters/Generated/TestDoubles.g.cs", persistence: "MemoryModelFilters/Persistence/Repositories.g.cs");
        Generate(filteredDrafts, new FilteredDraftsRepositoryDesigner().CreateModel(), "MemoryModelWrites", contracts: "Contracts/Generated/FilteredDraftsRepositories.g.cs",
            testDoubles: "MemoryModelWrites/Generated/TestDoubles.g.cs", persistence: "MemoryModelWrites/Persistence/Repositories.g.cs");
        Generate(autoLoadedCatalog, new ExplicitLoadingRepositoryDesigner().CreateModel(), "MemoryLoadingPolicies", contracts: "Contracts/Generated/AutoLoadedCatalogRepositories.g.cs",
            testDoubles: "MemoryLoadingPolicies/Generated/TestDoubles.g.cs", persistence: "MemoryLoadingPolicies/Persistence/Repositories.g.cs");
        Generate(defaultSettings, new DefaultSettingsRepositoryDesigner().CreateModel(), "MemoryModelDefaults", contracts: "Contracts/Generated/DefaultSettingsRepositories.g.cs",
            testDoubles: "MemoryModelDefaults/Generated/TestDoubles.g.cs", persistence: "MemoryModelDefaults/Persistence/Repositories.g.cs");
        Generate(filteredShelves, new FilteredShelfRepositoryDesigner().CreateModel(), "MemoryFilteredNavigation", contracts: "Contracts/Generated/FilteredShelfRepositories.g.cs",
            testDoubles: "MemoryFilteredNavigation/Generated/TestDoubles.g.cs", persistence: "MemoryFilteredNavigation/Persistence/Repositories.g.cs");
        Generate(savedShelves, new SavedShelfRepositoryDesigner().CreateModel(), "MemorySavedFilters", contracts: "Contracts/Generated/SavedShelfRepositories.g.cs",
            testDoubles: "MemorySavedFilters/Generated/TestDoubles.g.cs", persistence: "MemorySavedFilters/Persistence/Repositories.g.cs");
        Generate(nativeNotebook, new NativeNotebookRepositoryDesigner().CreateModel(), "MemoryNativeFilters", contracts: "Contracts/Generated/NativeNotebookRepositories.g.cs",
            testDoubles: "MemoryNativeFilters/Generated/TestDoubles.g.cs", persistence: "MemoryNativeFilters/Persistence/Repositories.g.cs");
        Generate(notes, new SimpleRepositoryDesigner().CreateModel(), "Simple");
        Generate(catalog, new CatalogRepositoryDesigner().CreateModel(), "Configured", projections: "Configured/Generated/Queries.g.cs");
        Generate(reporting, new ReportingRepositoryDesigner().CreateModel(), "Reports", contracts: "Contracts/Generated/ReportingRepositories.g.cs");
        Generate(library, new LibraryRepositoryDesigner().CreateModel(), "Library", contracts: "Contracts/Generated/LibraryRepositories.g.cs", apiSnapshot: true);
        Generate(ledger, new LedgerRepositoryDesigner().CreateModel(), "Ledger", contracts: "Contracts/Generated/Ledger",
            layout: RepositoryOutputLayout.PerContract, apiBaseline: "Contracts/ApiBaselines/LedgerApi.json");
        Generate(inventory, new InventoryRepositoryDesigner().CreateModel(), "Inventory", contracts: "Contracts/Generated/InventoryRepositories.g.cs", apiSnapshot: true);
        Generate(collections, new ChecklistRepositoryDesigner().CreateModel(), "Collections", contracts: "Contracts/Generated/CollectionsRepositories.g.cs");
        Generate(writeSafety, new AuditedNotesRepositoryDesigner().CreateModel(), "WriteSafety", contracts: "Contracts/Generated/WriteSafetyRepositories.g.cs");
        Generate(batchWrites, new BatchNotesRepositoryDesigner().CreateModel(), "BatchWrites", contracts: "Contracts/Generated/BatchWritesRepositories.g.cs");
        Generate(workboard, new WorkboardRepositoryDesigner().CreateModel(), "Cqrs", projections: "Cqrs/Generated/Queries.g.cs");
        Generate(taskDesk, new TaskDeskRepositoryDesigner().CreateModel(), "Adapters", contracts: "Contracts/Generated/TaskDeskRepositories.g.cs", apiSnapshot: true);
        Generate(directory, new LookupDirectoryRepositoryDesigner().CreateModel(), "AccessorAdapters", projections: "AccessorAdapters/Generated/Queries.g.cs");
        Generate(scriptedNotes, new ScriptedNotesRepositoryDesigner().CreateModel(), "ScriptedPorts", contracts: "Contracts/Generated/ScriptedNotesRepositories.g.cs",
            testDoubles: "ScriptedPorts/Generated/TestDoubles.g.cs", persistence: "ScriptedPorts/Persistence/Repositories.g.cs");
        Generate(scriptedFeed, new ScriptedFeedRepositoryDesigner().CreateModel(), "ScriptedStreams", contracts: "Contracts/Generated/ScriptedFeedRepositories.g.cs",
            testDoubles: "ScriptedStreams/Generated/TestDoubles.g.cs", persistence: "ScriptedStreams/Persistence/Repositories.g.cs");
        Generate(memoryNotebook, new MemoryNotebookRepositoryDesigner().CreateModel(), "MemoryFixtures", contracts: "Contracts/Generated/MemoryNotebookRepositories.g.cs",
            testDoubles: "MemoryFixtures/Generated/TestDoubles.g.cs", persistence: "MemoryFixtures/Persistence/Repositories.g.cs");
        Generate(memoryPartitions, new PartitionDirectoryRepositoryDesigner().CreateModel(), "MemoryPartitions", contracts: "Contracts/Generated/MemoryPartitionsRepositories.g.cs",
            testDoubles: "MemoryPartitions/Generated/TestDoubles.g.cs", persistence: "MemoryPartitions/Persistence/Repositories.g.cs");
        Generate(memoryCatalog, new MemoryCatalogRepositoryDesigner().CreateModel(), "MemoryCriteria", contracts: "Contracts/Generated/MemoryCatalogRepositories.g.cs",
            testDoubles: "MemoryCriteria/Generated/TestDoubles.g.cs", persistence: "MemoryCriteria/Persistence/Repositories.g.cs");
        Generate(memoryCoverage, new MemoryCoverageRepositoryDesigner().CreateModel(), "MemoryCoverage", contracts: "Contracts/Generated/MemoryCoverageRepositories.g.cs",
            testDoubles: "MemoryCoverage/Generated/TestDoubles.g.cs", persistence: "MemoryCoverage/Persistence/Repositories.g.cs");
        Generate(contractNotes, new ContractNotesRepositoryDesigner().CreateModel(), "RelationalContractFixture", contracts: "Contracts/Generated/ContractNotesRepositories.g.cs",
            testDoubles: "RelationalContractFixture/Generated/TestDoubles.g.cs");
        Generate(fixtureTickets, new FixtureTicketsRepositoryDesigner().CreateModel(), "RelationalWrites", contracts: "Contracts/Generated/FixtureTicketsRepositories.g.cs");
        Generate(orderReports, new OrderReportsRepositoryDesigner().CreateModel(), "MemoryJoins", contracts: "Contracts/Generated/OrderReportsRepositories.g.cs",
            testDoubles: "MemoryJoins/Generated/TestDoubles.g.cs", persistence: "MemoryJoins/Persistence/Repositories.g.cs");
        Generate(proxyDirectory, new ProxyDirectoryRepositoryDesigner().CreateModel(), "MemoryProxies", contracts: "Contracts/Generated/ProxyDirectoryRepositories.g.cs",
            testDoubles: "MemoryProxies/Generated/TestDoubles.g.cs", persistence: "MemoryProxies/Persistence/Repositories.g.cs");
        Generate(navigationCatalog, new NavigationCatalogRepositoryDesigner().CreateModel(), "MemoryNavigation", contracts: "Contracts/Generated/NavigationCatalogRepositories.g.cs",
            testDoubles: "MemoryNavigation/Generated/TestDoubles.g.cs", persistence: "MemoryNavigation/Persistence/Repositories.g.cs");
        using var membershipDirectory = new MembershipDirectoryContext(Options<MembershipDirectoryContext>());
        Generate(membershipDirectory, new MembershipDirectoryRepositoryDesigner().CreateModel(), "Membership", contracts: "Contracts/Generated/MembershipRepositories.g.cs");
        using var visibility = new VisibilityContext(Options<VisibilityContext>());
        Generate(visibility, new VisibilityRepositoryDesigner().CreateModel(), "Visibility", contracts: "Contracts/Generated/VisibilityRepositories.g.cs");
        using var primaryNotes = new PrimaryNoteContext(Options<PrimaryNoteContext>());
        using var archiveNotes = new ArchiveNoteContext(Options<ArchiveNoteContext>());
        var sharedPersistence = Path.Combine(root, "MultiContext/Generated");
        var multiple = RepositoryGenerator.GenerateMany(
            new(primaryNotes, new RepositoryOutputLocations(sharedPersistence, Projections: sharedPersistence,
                Manifest: Path.Combine(sharedPersistence, "Primary.manifest.json")), new PrimaryNoteRepositoryDesigner().CreateModel(), RepositoryOutputLayout.PerEntity),
            new(archiveNotes, new RepositoryOutputLocations(sharedPersistence, Path.Combine(root, "Contracts/Generated/MultiContext"),
                    Manifest: Path.Combine(sharedPersistence, "Archive.manifest.json"), ApiSnapshot: Path.Combine(sharedPersistence, "Archive.RepositoryApi.json")),
                new ArchiveNoteRepositoryDesigner().CreateModel(), RepositoryOutputLayout.PerContract, IncludeApiSnapshot: true));
        ValidateCompilation(multiple.ValidateCompilation);
        publish.Add(() =>
        {
            if (check)
            {
                foreach (var difference in multiple.Check())
                {
                    Console.Error.WriteLine($"{difference.DiagnosticId}: {difference.Path}: {difference.Message}");
                    stale = true;
                }

                Console.WriteLine($"MultiContext: checked {multiple.Bundles.Sum(static bundle => bundle.Files.Count)} generated files together.");
            }
            else
            {
                Console.WriteLine($"MultiContext: wrote {multiple.Write().Count} changed generated files together.");
            }
        });
        // All model/API/compiler checks finish before publishing any example's outputs.
        if (incompatibleApi)
        {
            return 4;
        }

        if (invalidCompilation)
        {
            return 5;
        }

        foreach (var action in publish)
        {
            action();
        }

        return stale ? 3 : 0;

        static void Usage() => Console.Error.WriteLine("Usage: GenerationRunner <ConfigurationExamples directory> [--check] [--compile-references <target-reference-list>]");

        static DbContextOptions<T> Options<T>() where T : DbContext => new DbContextOptionsBuilder<T>().UseSqlite("Data Source=:memory:").Options;

        void Generate(DbContext context, RepositoryDesign design, string example, string? contracts = null, string? projections = null,
            RepositoryOutputLayout layout = RepositoryOutputLayout.SingleFile, string? apiBaseline = null, bool apiSnapshot = false,
            string? testDoubles = null, string? persistence = null)
        {
            var split = layout != RepositoryOutputLayout.SingleFile;
            var output = new RepositoryOutputLocations(persistence is null ? Path.Combine(root, example, split ? "Generated" : "Generated/Repositories.g.cs") : Path.Combine(root, persistence),
                contracts is null ? null : Path.Combine(root, contracts), projections is null ? null : Path.Combine(root, projections),
                Path.Combine(root, example, "Generated/manifest.json"), apiBaseline is null && !apiSnapshot ? null : Path.Combine(root, example, "Generated/RepositoryApi.json"),
                testDoubles is null ? null : Path.Combine(root, testDoubles));
            var bundle = RepositoryGenerator.Generate(context, design, layout);
            if (apiSnapshot)
            {
                bundle = bundle.WithApiSnapshot();
            }

            if (apiBaseline is not null)
            {
                bundle = bundle.WithApiSnapshot();
                var baselinePath = Path.Combine(root, apiBaseline);
                if (File.Exists(baselinePath))
                {
                    var changes = bundle.CreateApiSnapshot().CompareTo(RepositoryApiSnapshot.Parse(File.ReadAllText(baselinePath)));
                    foreach (var change in changes)
                    {
                        Console.Error.WriteLine($"{change.DiagnosticId}: {change.Contract}: {change.Message} ({change.Impact})");
                    }

                    if (changes.Any(static change => change.IsBreaking))
                    {
                        incompatibleApi = true;
                        return;
                    }

                    Console.WriteLine($"{example}: approved API baseline is compatible.");
                }
                else if (check)
                {
                    Console.Error.WriteLine("RGA0001: Missing approved API baseline: " + baselinePath);
                    incompatibleApi = true;
                    return;
                }
                else
                {
                    Console.WriteLine($"{example}: initial candidate only; explicitly review/check in {apiBaseline}. Regeneration never writes that baseline.");
                }
            }

            ValidateCompilation(bundle.ValidateCompilation);
            publish.Add(() =>
            {
                if (check)
                {
                    foreach (var difference in bundle.Check(output))
                    {
                        Console.Error.WriteLine($"{difference.DiagnosticId}: {difference.Path}: {difference.Message}");
                        stale = true;
                    }

                    Console.WriteLine($"{example}: checked {bundle.Files.Count} generated files.");
                }
                else
                {
                    Console.WriteLine($"{example}: wrote {bundle.Write(output).Count} changed generated files.");
                }
            });
        }

        void ValidateCompilation(Func<RepositoryCompilationOptions, CancellationToken, RepositoryCompilationReport> validate)
        {
            if (compilation is null)
            {
                return;
            }

            var report = validate(compilation, CancellationToken.None);
            foreach (var diagnostic in report.Diagnostics)
            {
                Console.Error.WriteLine($"{diagnostic.Id}: {diagnostic.Stage}: {diagnostic.File}:{diagnostic.Line}:{diagnostic.Column}: {diagnostic.Message}");
            }

            if (!report.Succeeded)
            {
                invalidCompilation = true;
            }
        }
    }
}
