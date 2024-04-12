using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DocumentsService;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly IKafkaProducer producer;
    private readonly IOptions<KafkaOptions> kafkaOptions;

    public DocumentsController(IKafkaProducer producer, IOptions<KafkaOptions> kafkaOptions)
    {
        this.producer = producer;
        this.kafkaOptions = kafkaOptions;
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] Document document)
    {
        await producer.Produce(kafkaOptions.Value.OrdersTopicName, document);
        return Ok();
    }
}