using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Azure.AI.OpenAI;
using Azure.AI.Inference;
//using Azure.Search.Documents;
using OpenAI.Embeddings;
using OpenAI.Chat;
//using Azure.Search.Documents.Models;
using System.Net;

using Azure;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
//using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace AdminDashTemplate.Server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        public ChatController(IHttpClientFactory httpClientFactory)
        {
            _httpClient = _httpClient = httpClientFactory.CreateClient();
        }
        [HttpGet("Test")]
        public IActionResult Test()
        {
            var credential = new AzureKeyCredential("61arcclb5pyx9gzxWpyD6CYnSUYbWm6gYPDEIlnnGVlkQfGObgsoJQQJ99BGACYeBjFXJ3w3AAABACOGZ8AH");
            var openAIClient = new AzureOpenAIClient(new Uri("https://ncatoai.openai.azure.com/"), credential);
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _httpClient.DefaultRequestHeaders.Add("api-key", "IxykhFzO7hrknPTWcr6zALO4FRFDmDrdDGoEdczVehAzSeABC84h");
            return Content("chat controller is working");
        }
    }
}
