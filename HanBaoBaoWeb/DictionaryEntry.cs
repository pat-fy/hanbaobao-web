using Microsoft.AspNetCore.Mvc;
using Orleans;
using System.Threading.Tasks;
using Orleans.Runtime;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;

namespace HanBaoBao
{
    [ApiController]
    [Route("api/[controller]")]
    public class EntryController : ControllerBase
    {
        private readonly IGrainFactory _grainFactory;

        public EntryController(IGrainFactory grainFactory)
        {
            _grainFactory = grainFactory;
        }

        [HttpGet]
        public async Task<IActionResult> Get(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return BadRequest("Provided an invalid id");
            }

            var entryGrain = _grainFactory.GetGrain<IDictionaryEntryGrain>(id);
            var result = await entryGrain.GetDefinitionAsync();
            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody]TermDefinition entry)
        {
            if (string.IsNullOrWhiteSpace(entry?.Simplified))
            {
                return BadRequest("Provided an invalid entry");
            }

            var entryGrain = _grainFactory.GetGrain<IDictionaryEntryGrain>(entry.Simplified);
            await entryGrain.UpdateDefinitionAsync(entry);
            return Ok();
        }
    }

    [GenerateSerializer]
    public class TermDefinition
    {        
        [Id(0)] public long Id { get; set; }
        [Id(1)] public string Simplified { get; set; } = "";
        [Id(2)] public string Traditional { get; set; } = "";
        [Id(3)] public string Pinyin { get; set; } = "";
        [Id(4)] public string Definition { get; set; } = "";
        [Id(5)] public string Classifier { get; set; } = "";
        [Id(6)] public string Concept { get; set; } = "";
        [Id(7)] public int HskLevel { get; set; }
        [Id(8)] public string Topic { get; set; } = "";
        [Id(9)] public string ParentTopic { get; set; } = "";
        [Id(10)] public string Notes { get; set; } = "";
        [Id(11)] public double Frequency { get; set; }
        [Id(12)] public List<string> PartOfSpeech { get; set; } = new List<string>();
    }

    internal interface IDictionaryEntryGrain : IGrainWithStringKey
    {
        Task<TermDefinition> GetDefinitionAsync();
        Task UpdateDefinitionAsync(TermDefinition value);
    }

    internal class DictionaryEntryGrain : Grain, IDictionaryEntryGrain
    {
        private readonly IPersistentState<DictionaryEntryState> _state;
        private readonly ReferenceDataService _referenceDataService;

        public DictionaryEntryGrain(
            // Inject some storage. We will use the "definitions" storage provider configured in Program.cs
            // and we will call this piece of state "def", to distinguish it from any other state we might want to have
            [PersistentState(stateName: "def", storageName: "definitions")]
                IPersistentState<DictionaryEntryState> defs,
            ReferenceDataService referenceDataService)
        {
            _state = defs;
            _referenceDataService = referenceDataService;
        }

        public override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            // Initialize state if it's null
            if (_state.State is null)
            {
                _state.State = new DictionaryEntryState();
            }

            // If there is no state saved for this entry yet, load the state from the reference dictionary and store it.
            if (_state.State.Definition is null)
            {
                // Find the definiton from the reference data, using this grain's id to look it up
                var headword = this.GetPrimaryKeyString();
                var result = await _referenceDataService.QueryByHeadwordAsync(headword);

                if (result is { Count: > 0 } && result.FirstOrDefault() is TermDefinition definition)
                {
                    _state.State.Definition = definition;

                    // Write the state but don't wait for completion. If it fails, we will write it next time. 
                    _state.WriteStateAsync().Ignore();
                }
            }
        }

        public async Task UpdateDefinitionAsync(TermDefinition value)
        {
            if (!string.Equals(value.Simplified, this.GetPrimaryKeyString(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Cannot change the headword for a definition");
            }

            _state.State.Definition = value;
            await _state.WriteStateAsync();
        }

        public Task<TermDefinition> GetDefinitionAsync()
            => Task.FromResult(_state.State.Definition ?? new TermDefinition());
    }

    [GenerateSerializer]
    internal class DictionaryEntryState
    {
        [Id(0)] public TermDefinition? Definition { get; set; }
    }
}
