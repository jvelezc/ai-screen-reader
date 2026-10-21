namespace HeadlessScreenReader.Cli;

/// <summary>Fixed proof protocol. No general discovery or public authorization-server metadata.</summary>
internal static class PrivateViewerOpenApi
{
    public static string ForPixels(bool enabled)
    {
        if (!enabled) return Document;
        var root = System.Text.Json.Nodes.JsonNode.Parse(Document)!;
        root["info"]!["version"] = "1.1.0";
        root["paths"]!["/viewer/{scanId}/{sessionId}/pixels"] = System.Text.Json.Nodes.JsonNode.Parse("""
            {"get":{"operationId":"ReadPrivateViewerPixels","summary":"Fresh opted-in fixed-fixture PNG; not a retained observation or goal verification","security":[{"ProcessIdentity":[],"ConnectionId":[],"ConnectionToken":[]}],"parameters":[{"$ref":"#/components/parameters/ScanId"},{"$ref":"#/components/parameters/SessionId"},{"name":"epoch","in":"query","required":true,"schema":{"type":"integer","minimum":1}}],"responses":{"200":{"description":"320x240 PNG, at most 4 MiB. X-Viewer-Epoch, X-Viewer-Baseline-Ordinal, X-Viewer-Document-Sha256 and X-Viewer-Simulated-Assistance bind actual pixels to the owner baseline. No input or clipboard endpoint.","content":{"image/png":{"schema":{"type":"string","contentEncoding":"binary"}}}},"400":{"description":"Invalid or stale epoch, query or body"},"401":{"description":"Identity or scoped connection denied"},"403":{"description":"Client context denied"},"409":{"description":"Capture excluded, busy, stale or unavailable"}}}}
            """);
        return root.ToJsonString();
    }
    // Explicit protocol document avoids adding an OpenAPI generator package to the pinned prototype.
    public const string Document = """
    {
      "openapi":"3.1.0",
      "info":{"title":"HeadlessScreenReader private viewer proof","version":"1.0.0","description":"Loopback-only header-capable clients, process-provisioned identity and retained read-only observations. No public or tenant identity provider and no input authority."},
      "security":[{"ProcessIdentity":[]}],
      "paths":{
        "/viewer/exchange":{"post":{"operationId":"ExchangePrivateViewerBootstrap","summary":"Exchange one scoped bootstrap once","requestBody":{"required":true,"content":{"application/json":{"schema":{"$ref":"#/components/schemas/ExchangeViewerRequest"}}}},"responses":{"200":{"description":"Ephemeral read credential","content":{"application/json":{"schema":{"$ref":"#/components/schemas/ViewerConnectionResponse"}}}},"413":{"description":"Request body exceeds the proof limit"},"415":{"description":"JSON content type required"},"400":{"description":"Invalid request"},"401":{"description":"Identity, scope or bootstrap denied"},"403":{"description":"Client context denied"}}}},
        "/viewer/{scanId}/{sessionId}/observation":{"get":{"operationId":"ReadPrivateViewerObservation","summary":"Last acknowledged permitted observation; no new capture or input","security":[{"ProcessIdentity":[],"ConnectionId":[],"ConnectionToken":[]}],"parameters":[{"$ref":"#/components/parameters/ScanId"},{"$ref":"#/components/parameters/SessionId"},{"name":"epoch","in":"query","required":true,"schema":{"type":"integer","minimum":1}}],"responses":{"200":{"description":"Retained observation","content":{"application/json":{"schema":{"$ref":"#/components/schemas/ViewerObservationResponse"}}}},"400":{"description":"Malformed or stale epoch"},"401":{"description":"Identity, scope or connection denied"},"403":{"description":"Client context denied"},"409":{"description":"Observation unavailable, excluded, busy or revoked during delivery"}}}},
        "/viewer/{scanId}/{sessionId}/revoke":{"post":{"operationId":"RevokePrivateViewerConnection","summary":"Revoke read admission and drain actual work","security":[{"ProcessIdentity":[],"ConnectionId":[],"ConnectionToken":[]}],"parameters":[{"$ref":"#/components/parameters/ScanId"},{"$ref":"#/components/parameters/SessionId"}],"responses":{"204":{"description":"Actual source and owned delivery drained"},"400":{"description":"Unexpected body or query"},"401":{"description":"Identity, scope or connection denied"},"403":{"description":"Client context denied"},"409":{"description":"Stop unconfirmed"}}}}
      },
      "components":{
        "securitySchemes":{"ProcessIdentity":{"type":"http","scheme":"bearer"},"ConnectionId":{"type":"apiKey","in":"header","name":"X-Viewer-Id"},"ConnectionToken":{"type":"apiKey","in":"header","name":"X-Viewer-Token"}},
        "parameters":{"ScanId":{"name":"scanId","in":"path","required":true,"schema":{"type":"string","pattern":"^[a-f0-9]{32}$"}},"SessionId":{"name":"sessionId","in":"path","required":true,"schema":{"type":"string","pattern":"^[a-f0-9]{32}$"}}},
        "schemas":{
          "ExchangeViewerRequest":{"type":"object","additionalProperties":false,"required":["ScanId","SessionId","Bootstrap"],"properties":{"ScanId":{"type":"string"},"SessionId":{"type":"string"},"Bootstrap":{"type":"string","pattern":"^[a-f0-9]{64}$"}}},
          "ViewerConnectionResponse":{"type":"object","required":["ConnectionId","Token","ExpiresAt"],"properties":{"ConnectionId":{"type":"string"},"Token":{"type":"string","description":"Sensitive ephemeral read credential; never store in reports or logs"},"ExpiresAt":{"type":"string","format":"date-time"}}},
          "ViewerObservationResponse":{"type":"object","required":["ScanId","SessionId","Epoch","Ordinal","ObservationKind","Snapshot","PriorSimulatedAssistance"],"properties":{"ScanId":{"type":"string"},"SessionId":{"type":"string"},"Epoch":{"type":"integer"},"Ordinal":{"type":"integer"},"ObservationKind":{"const":"LastPersistedObservation"},"Snapshot":{"type":"object","description":"Source-tagged immutable normalized observation with null document URL; no editable values. Previously acknowledged, not freshly captured."},"PriorSimulatedAssistance":{"type":"boolean"}}}
        }
      }
    }
    """;
}
