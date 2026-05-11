import axios from "axios";
import { Client } from "pg";
import { mysteryClient } from "missing-sdk";
import type { User, UserId } from "../models/user";

export async function loadUser(id: UserId): Promise<User | null> {
  await axios.get(`/users/${id}`);
  const client = new Client();
  await client.query("select * from users where id = $1", [id]);
  return mysteryClient.get(id) as User | null;
}

export const loaderName = "load-user";
