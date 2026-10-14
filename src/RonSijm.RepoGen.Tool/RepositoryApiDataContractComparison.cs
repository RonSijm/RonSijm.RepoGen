namespace RonSijm.RepoGen.Tool;

internal static class RepositoryApiDataContractComparison
{
    public static IReadOnlyList<RepositoryApiDifference> Compare(IReadOnlyList<RepositoryApiDataContract>? candidate,
        IReadOnlyList<RepositoryApiDataContract>? baseline, RepositoryApiCompatibilityMode mode, string context)
    {
        var differences = new List<RepositoryApiDifference>();

        void Add(string id, string type, string? member, string message, RepositoryApiChangeImpact impact = RepositoryApiChangeImpact.Breaking)
            => differences.Add(new(id, type, member, message, impact));

        if (baseline is not null && candidate is null)
        {
            Add("RGA0004", context, null, "The approved baseline tracks data contracts. IncludeApiDataContracts must stay enabled; do not silently disable those checks.");
            return differences;
        }

        if (baseline is null)
        {
            if (candidate is not null)
            {
                Add("RGA0010", context, null, "Data-contract checks newly enabled. Review and explicitly approve this initial type inventory.", RepositoryApiChangeImpact.Advisory);
            }

            return differences;
        }

        var current = candidate!.ToDictionary(static contract => contract.Type, StringComparer.Ordinal);
        foreach (var old in baseline)
        {
            if (!current.TryGetValue(old.Type, out var contract))
            {
                Add("RGA1101", old.Type, null, "Previously exposed data contract removed, renamed or no longer reachable from the repository API.");
                continue;
            }

            if (old.Kind != contract.Kind || old.EnumUnderlyingType != contract.EnumUnderlyingType || old.Flags != contract.Flags ||
                old.GenericParameters.Count != contract.GenericParameters.Count || old.GenericParameters.Where((parameter, index) =>
                    index < contract.GenericParameters.Count && (parameter.Attributes != contract.GenericParameters[index].Attributes ||
                                                                 parameter.NullableConstraint != contract.GenericParameters[index].NullableConstraint ||
                                                                 !parameter.Constraints.SequenceEqual(contract.GenericParameters[index].Constraints))).Any())
            {
                Add("RGA1102", old.Type, null, "Data kind, enum representation, or generic constraints changed.");
            }

            if (!old.Abstract && contract.Abstract || !old.Sealed && contract.Sealed)
            {
                Add("RGA1102", old.Type, null, "Data type became abstract or sealed; construction/derived callers can break.");
            }

            foreach (var parent in old.Bases.Except(contract.Bases, StringComparer.Ordinal))
            {
                Add("RGA1102", old.Type, null, "Data base type removed: " + parent);
            }

            if (mode == RepositoryApiCompatibilityMode.ConsumersAndImplementers && old.Kind == RepositoryApiDataKind.Interface)
            {
                foreach (var parent in contract.Bases.Except(old.Bases, StringComparer.Ordinal))
                {
                    Add("RGA1110", old.Type, null, "New data-interface base may add implementer obligations: " + parent);
                }
            }

            var members = contract.Members.ToDictionary(static member => member.Name, StringComparer.Ordinal);
            foreach (var prior in old.Members)
            {
                if (!members.TryGetValue(prior.Name, out var member))
                {
                    Add("RGA1103", old.Type, prior.Name, "Public data member removed or renamed.");
                    continue;
                }

                if (prior.Kind != member.Kind || prior.Type != member.Type || prior.WriteType != member.WriteType && prior.Writable && member.Writable)
                {
                    Add("RGA1104", old.Type, prior.Name, "Data member kind/type or read/write nullable annotations changed.");
                }

                if (prior.Readable && !member.Readable || prior.Writable && !member.Writable || !prior.InitOnly && member.InitOnly)
                {
                    Add("RGA1105", old.Type, prior.Name, "Public data access removed or mutation restricted to initialization.");
                }

                if (!prior.Required && member.Required)
                {
                    Add("RGA1106", old.Type, prior.Name, "Data member is now required during construction.");
                }
            }

            foreach (var member in contract.Members.Where(member => !membersInOld(member)))
            {
                if (member.Required)
                {
                    Add("RGA1106", old.Type, member.Name, "New required data member can break existing object initializers.");
                }
                else if (old.Kind == RepositoryApiDataKind.Interface && mode == RepositoryApiCompatibilityMode.ConsumersAndImplementers)
                {
                    Add("RGA1110", old.Type, member.Name, "New data-interface member can break handwritten implementers.");
                }
                else
                {
                    Add("RGA1113", old.Type, member.Name, "New public data member; review serialized payloads and consumer expectations.", RepositoryApiChangeImpact.Advisory);
                }
            }

            bool membersInOld(RepositoryApiDataMember member) => old.Members.Any(prior => prior.Name == member.Name);
            var constructors = contract.Constructors.ToDictionary(static constructor => constructor.Signature, StringComparer.Ordinal);
            foreach (var prior in old.Constructors)
            {
                if (!constructors.TryGetValue(prior.Signature, out var constructor))
                {
                    Add("RGA1107", old.Type, prior.Signature, "Public constructor removed or input CLR signature changed.");
                    continue;
                }

                if (prior.SetsRequiredMembers && !constructor.SetsRequiredMembers)
                {
                    Add("RGA1106", old.Type, prior.Signature, "Constructor no longer satisfies required members.");
                }

                if (prior.Parameters.Count != constructor.Parameters.Count)
                {
                    Add("RGA1107", old.Type, prior.Signature, "Constructor parameters changed.");
                    continue;
                }

                for (var index = 0; index < prior.Parameters.Count; index++)
                {
                    var before = prior.Parameters[index];
                    var after = constructor.Parameters[index];
                    if (before.Type != after.Type || before.Name != after.Name || before.Optional && !after.Optional)
                    {
                        Add("RGA1108", old.Type, prior.Signature, $"Constructor parameter '{before.Name}' type/nullability, name or optionality changed.");
                    }
                    else if (before.Optional && after.Optional && before.DefaultValue != after.DefaultValue)
                    {
                        Add("RGA1114", old.Type, prior.Signature, $"Constructor parameter '{before.Name}' default changed.", RepositoryApiChangeImpact.Advisory);
                    }
                }
            }

            foreach (var constructor in contract.Constructors.Where(constructor => old.Constructors.Count > 0 && old.Constructors.All(prior => prior.Signature != constructor.Signature)))
            {
                Add("RGA1115", old.Type, constructor.Signature, "New constructor overload; review null/default-literal overload resolution.", RepositoryApiChangeImpact.Advisory);
            }

            var values = contract.EnumValues.ToDictionary(static value => value.Name, StringComparer.Ordinal);
            foreach (var prior in old.EnumValues)
            {
                if (!values.TryGetValue(prior.Name, out var value) || prior.Value != value.Value)
                {
                    Add("RGA1109", old.Type, prior.Name, "Enum name removed/renamed or numeric value changed.");
                }
            }

            foreach (var value in contract.EnumValues.Where(value => old.EnumValues.All(prior => prior.Name != value.Name)))
            {
                Add("RGA1112", old.Type, value.Name, "New enum value; review exhaustive handling and stored/serialized values.", RepositoryApiChangeImpact.Advisory);
            }

            for (var index = 0; index < Math.Min(old.GenericParameters.Count, contract.GenericParameters.Count); index++)
            {
                if (old.GenericParameters[index].Name != contract.GenericParameters[index].Name)
                {
                    Add("RGA1116", old.Type, null, "Generic parameter renamed; review source annotations.", RepositoryApiChangeImpact.Advisory);
                }
            }
        }

        return differences;
    }
}