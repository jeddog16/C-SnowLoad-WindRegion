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
    const container = env.AHD_API.getByName("default");
    return container.fetch(request);
  }
};
