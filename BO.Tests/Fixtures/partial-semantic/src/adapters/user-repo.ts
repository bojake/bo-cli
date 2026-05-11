import type { User, UserId } from "../models/user";

export class UserRepository {
  findById(id: UserId): User | null {
    return { id };
  }
}

export default UserRepository;
