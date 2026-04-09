import { Container } from "@cloudflare/containers";

export class AhdApiContainer extends Container {
  defaultPort = 8080;
  sleepAfter = "5m";

  envVars = {
    ASPNETCORE_ENVIRONMENT: "Production",
    ASPNETCORE_URLS: "http://+:8080",
    AppSettings__ApiKey: this.env.API_KEY
  };
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const apiKey = url.searchParams.get("api_key");
    let forwardedRequest = request;

    if (apiKey && !request.headers.get("X-API-Key")) {
      const headers = new Headers(request.headers);
      headers.set("X-API-Key", apiKey);
      forwardedRequest = new Request(request, { headers });
    }

    const container = env.AHD_API.getByName("default-v2");
    return container.fetch(forwardedRequest);
  }
};
