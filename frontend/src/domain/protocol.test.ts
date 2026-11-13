import { describe, expect, it } from "vitest";
import { protocolOf } from "./protocol";

describe("protocolOf", () => {
  it("speaks Sql to a database, whoever is asking", () => {
    expect(protocolOf("Service", "Database")).toBe("Sql");
    expect(protocolOf("Cache", "Database")).toBe("Sql");
    expect(protocolOf("Observability", "Database")).toBe("Sql");
  });

  it("speaks Amqp whenever a queue or a topic is at either end", () => {
    expect(protocolOf("Api", "Queue")).toBe("Amqp");
    expect(protocolOf("Queue", "Worker")).toBe("Amqp");
    expect(protocolOf("Service", "Topic")).toBe("Amqp");
    expect(protocolOf("Topic", "Service")).toBe("Amqp");
  });

  it("scrapes and pushes telemetry over Http, even from a queue", () => {
    expect(protocolOf("Queue", "Observability")).toBe("Http");
    expect(protocolOf("Observability", "Api")).toBe("Http");
  });

  it("is Http for everything else, and for an end it cannot see", () => {
    expect(protocolOf("Gateway", "Service")).toBe("Http");
    expect(protocolOf("Service", "Cache")).toBe("Http");
    expect(protocolOf("Api", "ExternalService")).toBe("Http");
    expect(protocolOf(undefined, undefined)).toBe("Http");
  });
});
