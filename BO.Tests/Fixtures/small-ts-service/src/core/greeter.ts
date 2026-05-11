export interface Greeter {
  greet(name: string): string;
}

export class FriendlyGreeter {
  constructor(prefix: string) {}

  greet(name: string) {
    return `${prefixValue()}${name}`;
  }

  format = (name: string) => name.toUpperCase();
}

const buildGreeting = (name: string) => `hi ${name}`;

function prefixValue() {
  return "hello ";
}

export { buildGreeting };
