using Xunit;

namespace ControlMenu.Common.Tests.Logging;

/// <summary>
/// xUnit v2 collection definition that serializes tests which mutate
/// process environment variables (e.g., PROGRAMDATA). Without this,
/// parallel test execution leaks env-var changes across tests.
/// </summary>
[CollectionDefinition("EnvVarSerialized", DisableParallelization = true)]
public class EnvVarSerializedCollection { }
