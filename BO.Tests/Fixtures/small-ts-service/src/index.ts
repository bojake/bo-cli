import { FriendlyGreeter, buildGreeting } from "./core/greeter";
import { handlePing } from "./http/handlers";

export { FriendlyGreeter, buildGreeting } from "./core/greeter";
export { handlePing } from "./http/handlers";

export default function bootstrap() {
  const greeter = new FriendlyGreeter("hi");
  greeter.greet("world");
  return handlePing("pong");
}
