namespace bondstone_backend.Application.Configuration.Schemas;

public class TopicConfiguration
{
    // QA 30-08 §11.4. Same as a queue's: the brokers a partitioned, replicated topic lives on.
    public int? Instances { get; set; }
}
